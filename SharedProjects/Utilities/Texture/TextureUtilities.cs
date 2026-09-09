using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Reflection;
using BabylonExport.Entities;

namespace Utilities
{
    static class TextureUtilities
    {
        public static List<string> validGltfFormats = new List<string>(new string[] { "png", "jpg", "jpeg" });
        public static List<string> invalidGltfFormats = new List<string>(new string[] { "dds", "tga", "tif", "tiff", "bmp", "gif" });
        public static readonly IEnumerable<TextureOperation> NoTransforms = Enumerable.Empty<TextureOperation>();

        // ------------------------------------------------------------------------------------------
        // Content-based format detection (UrbanCGI fork)
        //
        // Upstream trusted the file extension: png/jpg sources were copied byte-for-byte and declared
        // as image/png|jpeg, so a TGA renamed ".png" (which 3ds Max decodes happily) reached the GLB
        // verbatim and Babylon.js aborted the whole model load on the one undecodable image. Every
        // entry point in this class now asks for the file's REAL format and treats the extension as a
        // fallback that only matters when the bytes are not recognised.
        // ------------------------------------------------------------------------------------------

        /// <summary>One texture whose bytes disagreed with its extension during the current export.</summary>
        public sealed class TextureCorrection
        {
            public string SourcePath;
            /// <summary>Format token implied by the extension, e.g. "png".</summary>
            public string DeclaredFormat;
            /// <summary>Format token read from the bytes, e.g. "tga".</summary>
            public string ActualFormat;
            /// <summary>Format the texture was exported as ("png" or "jpg"), or null when it could not be exported.</summary>
            public string ExportedFormat;
        }

        private static readonly Dictionary<string, string> sniffCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> reportedMismatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<TextureCorrection> corrections = new List<TextureCorrection>();

        /// <summary>Textures corrected so far in the current export (reset by <see cref="BeginExport"/>).</summary>
        public static IReadOnlyList<TextureCorrection> Corrections { get { return corrections; } }

        /// <summary>Call once at the start of an export: forgets cached sniff results and the correction log.</summary>
        public static void BeginExport()
        {
            sniffCache.Clear();
            reportedMismatches.Clear();
            corrections.Clear();
        }

        /// <summary>The path's extension as a normalised format token ("jpg", "tif", ...), or "" when it has none.</summary>
        public static string ExtensionToken(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty);
            return string.IsNullOrEmpty(extension) ? string.Empty : ImageFormatSniffer.NormaliseToken(extension);
        }

        /// <summary>True when the two tokens / extensions name the same format ("jpeg" and ".jpg" do).</summary>
        public static bool SameFormat(string a, string b)
        {
            return string.Equals(ImageFormatSniffer.NormaliseToken(a), ImageFormatSniffer.NormaliseToken(b), StringComparison.Ordinal);
        }

        /// <summary>
        /// The file's real format token as read from its bytes ("png", "jpg", "tga", "dds", ...). Falls back to the
        /// extension token when the file is missing or its content is not recognised.
        /// </summary>
        public static string GetSourceImageFormat(string sourcePath)
        {
            string declared, actual;
            HasFormatMismatch(sourcePath, out declared, out actual);
            return actual ?? declared;
        }

        /// <summary>
        /// True when the bytes of <paramref name="sourcePath"/> are a recognised image format that differs from its
        /// extension. <paramref name="declared"/> is the extension token; <paramref name="actual"/> the sniffed token,
        /// or null when the content was not recognised or the file does not exist.
        /// </summary>
        public static bool HasFormatMismatch(string sourcePath, out string declared, out string actual)
        {
            declared = ExtensionToken(sourcePath);
            actual = null;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return false;
            }

            // Keyed on size + modification time so a file fixed between two exports is sniffed again.
            string key;
            try
            {
                var info = new FileInfo(sourcePath);
                key = sourcePath + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception)
            {
                key = sourcePath;
            }
            string sniffed;
            if (!sniffCache.TryGetValue(key, out sniffed))
            {
                sniffed = ImageFormatSniffer.Detect(sourcePath);
                sniffCache[key] = sniffed;
            }
            actual = sniffed;
            return sniffed != null && !string.Equals(sniffed, declared, StringComparison.Ordinal);
        }

        /// <summary>
        /// The glTF-safe format ("png" or "jpg") a texture is exported as, decided from the file's real content rather
        /// than its extension. Logs one warning per file when the two disagree. Null when the format is unsupported.
        /// Use this instead of GetValidImageFormat(Path.GetExtension(path)).
        /// </summary>
        public static string GetValidImageFormatForFile(string sourcePath, ILoggingProvider logger)
        {
            return GetValidImageFormatForFile(sourcePath, logger, null);
        }

        /// <param name="context">Extra words for the warning, e.g. " (map 'Weldlock_DIFFUSE.png')".</param>
        public static string GetValidImageFormatForFile(string sourcePath, ILoggingProvider logger, string context)
        {
            string declared, actual;
            bool mismatch = HasFormatMismatch(sourcePath, out declared, out actual);
            var token = mismatch ? actual : declared;
            var target = string.IsNullOrEmpty(token) ? null : _getValidImageFormat("." + token, validGltfFormats, invalidGltfFormats);
            if (mismatch)
            {
                ReportMismatch(sourcePath, declared, actual, target, logger, context);
            }
            return target;
        }

        private static void ReportMismatch(string sourcePath, string declared, string actual, string exportedAs, ILoggingProvider logger, string context)
        {
            if (!reportedMismatches.Add(sourcePath))
            {
                return;
            }
            corrections.Add(new TextureCorrection { SourcePath = sourcePath, DeclaredFormat = declared, ActualFormat = actual, ExportedFormat = exportedAs });
            if (logger == null)
            {
                return;
            }
            var outcome = exportedAs != null
                ? string.Format("It is exported as {0} this time", exportedAs.ToUpperInvariant())
                : "It cannot be exported";
            logger.RaiseWarning(string.Format(
                "Texture '{0}'{1} has a .{2} extension but its content is {3}. {4}. Fix the source file (re-save it as a real .{2}, or rename it .{5}) so the model stops depending on this correction. Path: {6}",
                Path.GetFileName(sourcePath), context ?? string.Empty, declared, actual.ToUpperInvariant(), outcome, actual, sourcePath), 3);
        }

        /// <summary>
        /// The bytes of <paramref name="sourcePath"/> ready to be embedded as <paramref name="targetFormat"/> ("png" or
        /// "jpeg"). When the file's real content already is that format the bytes are returned verbatim; otherwise (a
        /// TGA behind a .png name, a DDS, ...) the image is decoded with the right decoder and re-encoded. Only when the
        /// content cannot be decoded at all are the raw bytes returned, with an error logged.
        /// </summary>
        public static byte[] ReadImageBytes(string sourcePath, string targetFormat, long imageQuality, ILoggingProvider logger)
        {
            var real = GetSourceImageFormat(sourcePath);
            var target = ImageFormatSniffer.NormaliseToken(string.IsNullOrEmpty(targetFormat) ? "png" : targetFormat);
            if (string.Equals(real, target, StringComparison.Ordinal))
            {
                return File.ReadAllBytes(sourcePath);
            }

            Bitmap bitmap = null;
            try
            {
                bitmap = _convertToBitmap(sourcePath, NoTransforms, real, logger);
            }
            catch (Exception e)
            {
                logger.RaiseError(string.Format("Failed to decode texture {0} as {1}: {2}", Path.GetFileName(sourcePath), real.ToUpperInvariant(), e.Message), 3);
            }
            if (bitmap == null)
            {
                logger.RaiseError(string.Format("Texture {0} is embedded as-is; viewers may fail to load the model.", Path.GetFileName(sourcePath)), 3);
                return File.ReadAllBytes(sourcePath);
            }
            using (bitmap)
            using (var memory = new MemoryStream())
            {
                SaveBitmap(memory, bitmap, target == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png, imageQuality);
                return memory.ToArray();
            }
        }

#if !DONT_USE_PALOMA_TARGAIMAGE
        private static Bitmap LoadTarga(string path)
        {
            // The Stream overload ignores the file name, so a TGA hiding behind a ".png" extension decodes too.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return Paloma.TargaImage.LoadTargaImage(stream);
            }
        }
#endif

        public static string EncodeName(this IEnumerable<TextureOperation> operations)
        {
            // use System.Text namespace to avoid conflict with System.Drawing.Imaging namespace
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (var o in operations) sb.Append(o.Name);
            return sb.ToString();
        }

        public static void TransformTexture(string sourcePath, IEnumerable<TextureOperation> transforms, string destPath, long imageQuality, ILoggingProvider logger)
        {
            _copyTexture(sourcePath, transforms, destPath, imageQuality, validGltfFormats, invalidGltfFormats, logger);
        }

        public static Bitmap TransformTextureInPlace(this Bitmap source, IEnumerable<TextureOperation> transforms)
        {
            if (transforms.Count() != 0)
            {
                // Lock the bitmap's bits.  
                Rectangle rect = new Rectangle(0, 0, source.Width, source.Height);
                BitmapData data = source.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, source.PixelFormat);
                // Get the address of the first line.
                IntPtr ptr = data.Scan0;
                // Declare an array to hold the bytes of the bitmap.
                int bytes = Math.Abs(data.Stride) * data.Height;
                byte[] values = new byte[bytes];
                // Copy the values into the array.
                System.Runtime.InteropServices.Marshal.Copy(ptr, values, 0, bytes);

                foreach( var op in transforms)
                {
                    op.Apply(values, data);
                }
                // Copy the values back to the bitmap
                System.Runtime.InteropServices.Marshal.Copy(values, 0, ptr, bytes);

                // Unlock the bits.
                source.UnlockBits(data);
            }
            return source;
        }

        public static bool GetMinimalBitmapDimensions(out int width, out int height, params Bitmap[] bitmaps)
        {
            var haveSameDimensions = true;

            var bitmapsNoNull = ((new List<Bitmap>(bitmaps)).FindAll(bitmap => bitmap != null)).ToArray();
            if (bitmapsNoNull.Length > 0)
            {
                // Init with first element
                width = bitmapsNoNull[0].Width;
                height = bitmapsNoNull[0].Height;

                // Update with others
                for (int i = 1; i < bitmapsNoNull.Length; i++)
                {
                    var bitmap = bitmapsNoNull[i];
                    if (width != bitmap.Width || height != bitmap.Height)
                    {
                        haveSameDimensions = false;
                    }
                    width = Math.Min(width, bitmap.Width);
                    height = Math.Min(height, bitmap.Height);
                }
            }
            else
            {
                width = 0;
                height = 0;
            }

            return haveSameDimensions;
        }

        private static readonly ImageConverter _imageConverter = new ImageConverter();
        public static Bitmap LoadTexture(string absolutePath, ILoggingProvider logger)
        {
            if (File.Exists(absolutePath))
            {
                try
                {
                    // Decided from the bytes; the extension is only a fallback when the content is not recognised.
                    switch ("." + GetSourceImageFormat(absolutePath))
                    {
#if !DONT_USE_GDIMAGE_LIBRARY
                        case ".dds":
                                  // External library GDImageLibrary.dll + TQ.Texture.dll
                                  return GDImageLibrary._DDS.LoadImage(absolutePath);
#endif
#if !DONT_USE_PALOMA_TARGAIMAGE
                        case ".tga":
                            // External library TargaImage.dll
                            return LoadTarga(absolutePath);
#endif
                        case ".bmp":
                        case ".gif":
                        case ".jpg":
                        case ".jpeg":
                        case ".png":
                        case ".tif":
                        case ".tiff":
                            {
                                return (Bitmap)_imageConverter.ConvertFrom(File.ReadAllBytes(absolutePath));
                            }
                        default:
                            logger.RaiseError(string.Format("Format of texture {0} is not supported by the exporter. Consider using a standard image format like jpg or png.", Path.GetFileName(absolutePath)), 3);
                            return null;
                    }
                }
                catch (Exception e)
                {
                    logger.RaiseError(string.Format("Failed to load texture {0}: {1}", Path.GetFileName(absolutePath), e.Message), 3);
                    return null;
                }
            }
            else
            {
                logger.RaiseError(string.Format("Texture {0} not found.", absolutePath), 3);
                return null;
            }
        }

        // https://dejanstojanovic.net/aspnet/2014/june/getting-systemdrawingimagingimageformat-from-a-string/
        public static ImageFormat GetImageFormat(string extension)
        {
            ImageFormat result = null;
            if (extension == null || extension == "")
            {
                return result;
            }

            if (extension[0] == '.' || extension[0] == ',')
            {
                extension = extension.Substring(1);
            }

            if (extension == "jpg")
            {
                extension = "jpeg";
            }

            PropertyInfo prop = typeof(ImageFormat).GetProperties().Where(p => p.Name.Equals(extension, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            if (prop != null)
            {
                result = prop.GetValue(prop) as ImageFormat;
            }

            return result;
        }

        public static void CopyTexture(string sourcePath, string destPath, long imageQuality, ILoggingProvider logger)
        {
            _copyTexture(sourcePath, NoTransforms, destPath, imageQuality, validGltfFormats, invalidGltfFormats, logger);
        }

        public static void CopyTexture(string sourcePath, IEnumerable<TextureOperation> transforms, string destPath, long imageQuality, ILoggingProvider logger)
        {
            _copyTexture(sourcePath, transforms, destPath, imageQuality, validGltfFormats, invalidGltfFormats, logger);
        }

        public static Bitmap GetBitmap(string sourcePath, IEnumerable<TextureOperation> transforms, ILoggingProvider logger)
        {
            string imageFormat = GetSourceImageFormat(sourcePath); // the real content format, not the extension
            return _convertToBitmap(sourcePath, transforms, imageFormat, logger);
        }


        public static string GetValidImageFormat(string extension)
        {
            return _getValidImageFormat(extension, validGltfFormats, invalidGltfFormats);
        }

        public static bool ExtensionIsValidGLTFTexture(string _extension)
        {
            if (_extension.StartsWith("."))
            {
                _extension = _extension.Replace(".", "");
            }

            if (validGltfFormats.Contains(_extension))
            {
                return true;
            }

            return false;
        }

        private static string _getValidImageFormat(string extension, List<string> validFormats, List<string> invalidFormats)
        {
            var imageFormat = extension.Substring(1).ToLower(); // remove the dot

            if (validFormats.Contains(imageFormat))
            {
                return imageFormat;
            }
            else if (invalidFormats.Contains(imageFormat))
            {
                switch (imageFormat)
                {
                    case "dds":
                    case "tga":
                    case "tif":
                    case "tiff":
                    case "gif":
                    case "png":
                        return "png";
                    case "bmp":
                    case "jpg":
                    case "jpeg":
                        return "jpg";
                    default:
                        return null;
                }
            }
            else
            {
                return null;
            }
        }


        public static string GetPreferredFormat(string path, bool hasAlpha, TextureFormatExportPolicy policy = TextureFormatExportPolicy.CONSERVATIV)
        {
            if (hasAlpha) return "png";

            switch (policy)
            {
                case TextureFormatExportPolicy.CONSERVATIV:
                    {
                        if (!string.IsNullOrEmpty(path))
                        {
                            return GetValidImageFormat(path);
                        }
                        return "png";
                    }
                case TextureFormatExportPolicy.SIZE:
                    {
                        return "jpg";
                    }
                case TextureFormatExportPolicy.QUALITY:
                default:
                    {
                        return "png";
                    }
            }
        }
        public static string GetPreferredFormat(IEnumerable<string> paths, bool hasAlpha, TextureFormatExportPolicy policy = TextureFormatExportPolicy.QUALITY)
        {
            if (hasAlpha) return "png";

            switch (policy)
            {
                case TextureFormatExportPolicy.CONSERVATIV:
                    {
                        if (paths != null)
                        {
                            var exts = paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.GetExtension(p)).Select(e=> GetValidImageFormat(e));
                            return exts.Any(e => e.Equals("jpg")) ? "jpg" : "png";
                        }
                        return "png";
                    }
                case TextureFormatExportPolicy.SIZE:
                    {
                        return "jpg";
                    }
                case TextureFormatExportPolicy.QUALITY:
                default:
                    {
                        return "png";
                    }
            }
        }

        /// <summary>
        /// Copy image from source to dest.
        /// The copy process may include a conversion to another image format:
        /// - a source with a valid format is copied directly
        /// - a source with an invalid format is converted to png or jpg before being copied
        /// - a source with neither a valid nor an invalid format raises a warning and is not copied
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="destPath"></param>
        /// <param name="validFormats"></param>
        /// <param name="invalidFormats"></param>
        private static void _copyTexture(string sourcePath, IEnumerable<TextureOperation> transforms, string destPath, long imageQuality, List<string> validFormats, List<string> invalidFormats, ILoggingProvider logger)
        {
            try
            {
                if (File.Exists(sourcePath))
                {
                    // The REAL format of the bytes; the extension is only a fallback when the content is not recognised.
                    string imageFormat = GetSourceImageFormat(sourcePath);
                    // The format the destination name promises (png or jpg); callers derive it from the real format too.
                    string destFormat = ExtensionToken(destPath);

                    if (validFormats.Contains(imageFormat))
                    {
                        if (transforms.Count() == 0 && SameFormat(imageFormat, destFormat))
                        {
                            if (sourcePath != destPath)
                            {
                                File.Copy(sourcePath, destPath, true);
                            }
                        }
                        else
                        {
                            // Either a texture operation applies, or the bytes are not what the destination name says
                            // (e.g. a JPEG behind a .png name): decode with the right decoder and re-encode.
                            _convertToBitmapAndSave(sourcePath, transforms, destPath, imageFormat, imageQuality, logger);
                        }
                    }
                    else if (invalidFormats.Contains(imageFormat))
                    {
                        _convertToBitmapAndSave(sourcePath, transforms, destPath, imageFormat, imageQuality, logger);
                    }
                    else
                    {
                        logger.RaiseError(string.Format("Format of texture {0} is not supported by the exporter. Consider using a standard image format like jpg or png.", Path.GetFileName(sourcePath)), 3);
                    }
                }
                else logger.RaiseError(string.Format("Texture not found: {0}", sourcePath), 3);
            }
            catch (Exception c)
            {
                logger.RaiseError(string.Format("Exporting texture {0} failed: {1}", sourcePath, c.ToString()), 3);
            }
        }

        /// <summary>
        /// Load image from source to a bitmap and save it to dest as png or jpg.
        /// Loading process to a bitmap depends on extension.
        /// Saved image format depends on alpha presence.
        /// png and jpg are copied directly.
        /// Unsupported format raise a warning and are not copied.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="destPath"></param>
        /// <param name="imageFormat"></param>
        private static void _convertToBitmapAndSave(string sourcePath, IEnumerable<TextureOperation> transforms, string destPath, string imageFormat, long imageQuality, ILoggingProvider logger)
        {
            try
            {
                Bitmap bitmap = _convertToBitmap(sourcePath, transforms, imageFormat, logger);
                if (bitmap == null)
                {
                    return; // already reported by _convertToBitmap
                }
                // Save in the format the destination name promises (png or jpg). Callers derive that name from the
                // source's real format, so a TGA behind a .png name ends up as a genuine PNG; a BMP still becomes a JPG.
                var outputFormat = SameFormat(ExtensionToken(destPath), "jpg") ? ImageFormat.Jpeg : ImageFormat.Png;
                SaveBitmap(bitmap, destPath, outputFormat, imageQuality, logger);
            }
            catch (Exception e)
            {
                logger.RaiseError(string.Format("Failed to convert texture {0}: {1}", Path.GetFileName(sourcePath), e.Message), 3);
            }
        }

        public static Bitmap _convertToBitmap(string sourcePath, IEnumerable<TextureOperation> transforms, string imageFormat, ILoggingProvider logger)
        {
            switch (ImageFormatSniffer.NormaliseToken(imageFormat))
            {
#if !DONT_USE_GDIMAGE_LIBRARY
                case "dds":
                    // External libraries GDImageLibrary.dll + TQ.Texture.dll
                    try
                    {
                        return GDImageLibrary._DDS.LoadImage(sourcePath);
                    }
                    catch (Exception e)
                    {
                        logger.RaiseError(string.Format("Failed to convert texture {0} to png: {1}", Path.GetFileName(sourcePath), e.Message), 3);
                    }
                    break;
#endif
#if !DONT_USE_PALOMA_TARGAIMAGE
                case "tga":
                    {
                        // External library TargaImage.dll
                        try
                        {
                            return LoadTarga(sourcePath).TransformTextureInPlace(transforms);
                        }
                        catch (Exception e)
                        {
                            logger.RaiseError(string.Format("Failed to convert texture {0} to png: {1}", Path.GetFileName(sourcePath), e.Message), 3);
                        }
                        break;
                    }
#endif
                case "bmp":
                case "jpg":
                case "jpeg":
                case "png":
                case "tif":
                case "tiff":
                case "gif":
                    {
                        // GDI+ identifies these by content, so a mislabelled file decodes correctly here.
                        return new Bitmap(sourcePath).TransformTextureInPlace(transforms);
                    }
                default:
                    logger.RaiseWarning(string.Format("Format of texture {0} is not supported by the exporter. Consider using a standard image format like jpg or png.", Path.GetFileName(sourcePath)), 3);
                    break;
            }
            return null;
       }

        public static void SaveBitmap(Bitmap bitmap, string path, ImageFormat imageFormat, long imageQuality, ILoggingProvider logger)
        {
            SaveBitmap(bitmap, Path.GetDirectoryName(path), Path.GetFileName(path), imageFormat, imageQuality, logger);
        }

        public static void SaveBitmap(Bitmap bitmap, string directoryName, string fileName, ImageFormat imageFormat, long imageQuality, ILoggingProvider logger)
        {
            List<char> invalidCharsInString = GetInvalidChars(directoryName, Path.GetInvalidPathChars());
            if (invalidCharsInString.Count > 0)
            {
                logger.RaiseError($"Failed to save bitmap: directory name '{directoryName}' contains invalid character{(invalidCharsInString.Count > 1 ? "s" : "")} {invalidCharsInString.ToArray().ToString(false)}", 3);
                return;
            }
            invalidCharsInString = GetInvalidChars(fileName, Path.GetInvalidFileNameChars());
            if (invalidCharsInString.Count > 0)
            {
                logger.RaiseError($"Failed to save bitmap: file name '{fileName}' contains invalid character{(invalidCharsInString.Count > 1 ? "s" : "")} {invalidCharsInString.ToArray().ToString(false)}", 3);
                return;
            }

            string path = Path.Combine(directoryName, fileName);
            using (FileStream fs = File.Open(path, FileMode.Create))
            {

                SaveBitmap(fs, bitmap, imageFormat, imageQuality);
            }
        }
        
        public static void SaveBitmap(Stream output, Bitmap bitmap, ImageFormat imageFormat, long imageQuality)
        {
            ImageCodecInfo encoder = GetEncoder(imageFormat);

            if (encoder != null)
            {
                // Create an Encoder object based on the GUID for the Quality parameter category
                EncoderParameters encoderParameters = new EncoderParameters(1);
                EncoderParameter encoderQualityParameter = new EncoderParameter(Encoder.Quality, imageQuality);
                encoderParameters.Param[0] = encoderQualityParameter;

                bitmap.Save(output, encoder, encoderParameters);
            }
            else
            {
                bitmap.Save(output, imageFormat);
            }
        }


        private static List<char> GetInvalidChars(string s, char[] invalidChars)
        {
            List<char> invalidCharsInString = new List<char>();
            foreach (char ch in invalidChars)
            {
                int indexInvalidChar = s.IndexOf(ch);
                if (indexInvalidChar != -1)
                {
                    invalidCharsInString.Add(s[indexInvalidChar]);
                }
            }
            return invalidCharsInString;
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public static string ColorToStringName(Color color)
        {
            return "" + color.R + color.G + color.B + color.A;
        }

        public static string ColorToStringName(float[] color)
        {
            return "" + (int)(color[0] * 255) + (int)(color[1] * 255) + (int)(color[2] * 255);
        }
    }
}
