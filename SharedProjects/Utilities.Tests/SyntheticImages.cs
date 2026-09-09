using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace Utilities.Tests
{
    /// <summary>Small images built in memory so the tests need no binary fixtures.</summary>
    internal static class SyntheticImages
    {
        /// <summary>
        /// An uncompressed 32-bit true-colour TGA (image type 2) with a top-left origin. <paramref name="pixels"/> is
        /// row-major from the top-left corner. Optionally carries the TGA 2.0 footer.
        /// </summary>
        public static byte[] Tga32(int width, int height, Color[] pixels, bool withFooter)
        {
            var header = new byte[18];
            header[2] = 2;                       // uncompressed true-colour
            header[12] = (byte)(width & 0xFF);
            header[13] = (byte)(width >> 8);
            header[14] = (byte)(height & 0xFF);
            header[15] = (byte)(height >> 8);
            header[16] = 32;                     // bits per pixel
            header[17] = 0x28;                   // 8 alpha bits, top-left origin
            using (var stream = new MemoryStream())
            {
                stream.Write(header, 0, header.Length);
                foreach (var pixel in pixels)
                {
                    stream.WriteByte(pixel.B);
                    stream.WriteByte(pixel.G);
                    stream.WriteByte(pixel.R);
                    stream.WriteByte(pixel.A);
                }
                if (withFooter)
                {
                    stream.Write(new byte[8], 0, 8); // extension + developer offsets
                    var signature = Encoding.ASCII.GetBytes("TRUEVISION-XFILE.\0");
                    stream.Write(signature, 0, signature.Length);
                }
                return stream.ToArray();
            }
        }

        /// <summary>Only the 18-byte header of a run-length-encoded true-colour TGA (image type 10), plus a few bytes.</summary>
        public static byte[] TgaRleHeaderOnly(int width, int height)
        {
            var bytes = new byte[18 + 6];
            bytes[2] = 10;
            bytes[12] = (byte)(width & 0xFF);
            bytes[13] = (byte)(width >> 8);
            bytes[14] = (byte)(height & 0xFF);
            bytes[15] = (byte)(height >> 8);
            bytes[16] = 32;
            bytes[17] = 0x08;
            return bytes;
        }

        public static byte[] Encoded(int width, int height, Color fill, ImageFormat format)
        {
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        bitmap.SetPixel(x, y, fill);
                    }
                }
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, format);
                    return stream.ToArray();
                }
            }
        }

        /// <summary>Writes bytes under a fresh temp directory with the given file name and returns the full path.</summary>
        public static string WriteTemp(string fileName, byte[] bytes)
        {
            var directory = Path.Combine(Path.GetTempPath(), "UrbanX-Exporter-Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public static bool IsPng(byte[] bytes)
        {
            return bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        }

        public static bool IsJpeg(byte[] bytes)
        {
            return bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        }
    }
}
