using System;
using System.IO;
using System.Text;

namespace Utilities
{
    /// <summary>
    /// Identifies an image file's real format from its bytes rather than its extension.
    ///
    /// Why this exists: 3ds Max decodes bitmaps by content, so a TGA that was renamed ".png" renders
    /// perfectly in Max, while the exporter used to trust the extension and embed the raw TGA bytes in
    /// the GLB as image/png. Browsers cannot decode TGA, and Babylon.js then aborts the whole model
    /// load on that single image. Every texture entry point now asks this class what the bytes are.
    /// </summary>
    public static class ImageFormatSniffer
    {
        /// <summary>Bytes read from the start of a file; enough for every signature checked here.</summary>
        public const int HeaderLength = 32;

        /// <summary>Length of the TGA 2.0 footer, the only trailer that is inspected.</summary>
        public const int FooterLength = 26;

        private const string TgaSignature = "TRUEVISION-XFILE";

        /// <summary>
        /// The normalised format token of the file's content: "png", "jpg", "gif", "bmp", "tif", "dds",
        /// "tga", "webp", "psd", "exr", "hdr" or "ktx". Null when the bytes are not recognised or the file
        /// cannot be read.
        /// </summary>
        public static string Detect(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = stream.Length;
                    var head = new byte[HeaderLength];
                    int headLength = ReadFully(stream, head, (int)Math.Min(HeaderLength, length));
                    var foot = new byte[FooterLength];
                    int footLength = 0;
                    if (length >= FooterLength)
                    {
                        stream.Seek(length - FooterLength, SeekOrigin.Begin);
                        footLength = ReadFully(stream, foot, FooterLength);
                    }
                    return Detect(head, headLength, foot, footLength, length);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Same as <see cref="Detect(string)"/> for an image already in memory.</summary>
        public static string Detect(byte[] data)
        {
            if (data == null)
            {
                return null;
            }
            int headLength = Math.Min(HeaderLength, data.Length);
            var head = new byte[HeaderLength];
            Array.Copy(data, 0, head, 0, headLength);
            int footLength = Math.Min(FooterLength, data.Length);
            var foot = new byte[FooterLength];
            Array.Copy(data, data.Length - footLength, foot, 0, footLength);
            return Detect(head, headLength, foot, footLength, data.Length);
        }

        /// <summary>
        /// Maps an extension or token onto the spelling used by <see cref="Detect(string)"/>: lower case, no
        /// leading dot, "jpeg" becomes "jpg" and "tiff" becomes "tif".
        /// </summary>
        public static string NormaliseToken(string extensionOrToken)
        {
            if (string.IsNullOrEmpty(extensionOrToken))
            {
                return string.Empty;
            }
            var token = extensionOrToken.Trim().TrimStart('.').ToLowerInvariant();
            switch (token)
            {
                case "jpeg":
                    return "jpg";
                case "tiff":
                    return "tif";
                default:
                    return token;
            }
        }

        internal static string Detect(byte[] head, int headLength, byte[] foot, int footLength, long fileLength)
        {
            if (StartsWith(head, headLength, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "png";
            if (StartsWith(head, headLength, 0xFF, 0xD8, 0xFF)) return "jpg";
            if (StartsWithAscii(head, headLength, "GIF87a") || StartsWithAscii(head, headLength, "GIF89a")) return "gif";
            if (StartsWithAscii(head, headLength, "II*\0") || StartsWithAscii(head, headLength, "MM\0*")) return "tif";
            if (StartsWithAscii(head, headLength, "DDS ")) return "dds";
            if (StartsWithAscii(head, headLength, "RIFF") && headLength >= 12 && Ascii(head, 8, 4) == "WEBP") return "webp";
            if (StartsWithAscii(head, headLength, "8BPS")) return "psd";
            if (StartsWith(head, headLength, 0x76, 0x2F, 0x31, 0x01)) return "exr";
            if (StartsWithAscii(head, headLength, "#?RADIANCE") || StartsWithAscii(head, headLength, "#?RGBE")) return "hdr";
            if (StartsWith(head, headLength, 0xAB, 0x4B, 0x54, 0x58, 0x20)) return "ktx";
            if (StartsWithAscii(head, headLength, "BM") && LooksLikeBmp(head, headLength, fileLength)) return "bmp";
            // TGA has no magic number: trust the TGA 2.0 footer first, then a strict header plausibility check.
            if (HasTgaFooter(foot, footLength)) return "tga";
            if (LooksLikeTgaHeader(head, headLength, fileLength)) return "tga";
            return null;
        }

        private static bool LooksLikeBmp(byte[] head, int headLength, long fileLength)
        {
            if (headLength < 14)
            {
                return false;
            }
            long declaredSize = (uint)(head[2] | (head[3] << 8) | (head[4] << 16) | (head[5] << 24));
            // Some writers leave the size field at zero; otherwise it is the file length.
            return declaredSize == 0 || declaredSize == fileLength;
        }

        private static bool HasTgaFooter(byte[] foot, int footLength)
        {
            return footLength == FooterLength
                && Ascii(foot, 8, TgaSignature.Length) == TgaSignature
                && foot[24] == (byte)'.'
                && foot[25] == 0;
        }

        private static bool LooksLikeTgaHeader(byte[] h, int headLength, long fileLength)
        {
            if (headLength < 18)
            {
                return false;
            }
            int idLength = h[0];
            int colourMapType = h[1];
            int imageType = h[2];
            int colourMapFirst = h[3] | (h[4] << 8);
            int colourMapLength = h[5] | (h[6] << 8);
            int colourMapEntrySize = h[7];
            int width = h[12] | (h[13] << 8);
            int height = h[14] | (h[15] << 8);
            int depth = h[16];
            int descriptor = h[17];

            if (colourMapType > 1) return false;
            bool colourMapped = imageType == 1 || imageType == 9;
            bool trueColour = imageType == 2 || imageType == 10;
            bool greyscale = imageType == 3 || imageType == 11;
            if (!(colourMapped || trueColour || greyscale)) return false;
            if (width == 0 || height == 0) return false;
            if ((descriptor & 0xC0) != 0) return false; // reserved bits must be zero
            if (colourMapped != (colourMapType == 1)) return false;
            if (colourMapType == 1)
            {
                if (colourMapLength == 0) return false;
                if (!(colourMapEntrySize == 15 || colourMapEntrySize == 16 || colourMapEntrySize == 24 || colourMapEntrySize == 32)) return false;
            }
            else if (colourMapFirst != 0 || colourMapLength != 0 || colourMapEntrySize != 0)
            {
                return false;
            }
            if (trueColour && !(depth == 15 || depth == 16 || depth == 24 || depth == 32)) return false;
            if (greyscale && !(depth == 8 || depth == 16)) return false;
            if (colourMapped && !(depth == 8 || depth == 16)) return false;

            long colourMapBytes = colourMapType == 1 ? (long)colourMapLength * ((colourMapEntrySize + 7) / 8) : 0;
            long minimumLength = 18 + idLength + colourMapBytes;
            if (imageType < 9)
            {
                // Uncompressed: the whole pixel block has to fit in the file.
                minimumLength += (long)width * height * ((depth + 7) / 8);
            }
            return fileLength >= minimumLength;
        }

        private static bool StartsWith(byte[] data, int length, params byte[] signature)
        {
            if (length < signature.Length)
            {
                return false;
            }
            for (int i = 0; i < signature.Length; i++)
            {
                if (data[i] != signature[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool StartsWithAscii(byte[] data, int length, string signature)
        {
            return length >= signature.Length && Ascii(data, 0, signature.Length) == signature;
        }

        private static string Ascii(byte[] data, int offset, int count)
        {
            return Encoding.ASCII.GetString(data, offset, count);
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            return total;
        }
    }
}
