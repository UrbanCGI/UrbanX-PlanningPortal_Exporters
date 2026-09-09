using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// The texture pipeline as the exporter drives it: format decision, copy-or-convert to disk (glTF with external
    /// textures / .babylon), and bytes-for-embedding (GLB).
    /// </summary>
    [Collection("TextureUtilities static state")]
    public class TextureCorrectionTests
    {
        private static readonly Color[] Pixels = { Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 0, 255, 0), Color.FromArgb(255, 0, 0, 255), Color.FromArgb(128, 255, 255, 255) };
        private readonly ITestOutputHelper output;

        public TextureCorrectionTests(ITestOutputHelper output)
        {
            this.output = output;
            TextureUtilities.BeginExport();
        }

        [Fact]
        public void TgaBehindPngNameIsReportedOnceAndExportedAsPng()
        {
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("ML_Weldlock_surface_DIFFUSE.png", SyntheticImages.Tga32(2, 2, Pixels, withFooter: true));

            string declared, actual;
            Assert.True(TextureUtilities.HasFormatMismatch(source, out declared, out actual));
            Assert.Equal("png", declared);
            Assert.Equal("tga", actual);
            Assert.Equal("tga", TextureUtilities.GetSourceImageFormat(source));

            Assert.Equal("png", TextureUtilities.GetValidImageFormatForFile(source, logger, " (map 'Weldlock')"));
            Assert.Equal("png", TextureUtilities.GetValidImageFormatForFile(source, logger));
            Assert.Single(logger.Warnings);
            Assert.Contains("has a .png extension but its content is TGA", logger.Warnings[0]);
            Assert.Contains("(map 'Weldlock')", logger.Warnings[0]);
            Assert.Contains("exported as PNG", logger.Warnings[0]);
            Assert.Single(TextureUtilities.Corrections);
            Assert.Equal("png", TextureUtilities.Corrections[0].ExportedFormat);
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public void CopyTextureConvertsTgaBehindPngNameToARealPng()
        {
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("disguised.png", SyntheticImages.Tga32(2, 2, Pixels, withFooter: true));
            var destination = Path.Combine(Path.GetDirectoryName(source), "out", "disguised.png");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));

            TextureUtilities.CopyTexture(source, destination, 100, logger);

            var bytes = File.ReadAllBytes(destination);
            Assert.True(SyntheticImages.IsPng(bytes), "destination must be a genuine PNG");
            using (var decoded = new Bitmap(destination))
            {
                Assert.Equal(2, decoded.Width);
                Assert.Equal(2, decoded.Height);
                Assert.Equal(Pixels[0].ToArgb(), decoded.GetPixel(0, 0).ToArgb());
                Assert.Equal(Pixels[1].ToArgb(), decoded.GetPixel(1, 0).ToArgb());
                Assert.Equal(Pixels[2].ToArgb(), decoded.GetPixel(0, 1).ToArgb());
                Assert.Equal(Pixels[3].ToArgb(), decoded.GetPixel(1, 1).ToArgb());
            }
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public void CopyTextureStillCopiesAGenuinePngVerbatim()
        {
            var logger = new TestLogger();
            var png = SyntheticImages.Encoded(3, 3, Color.Orange, ImageFormat.Png);
            var source = SyntheticImages.WriteTemp("real.png", png);
            var destination = Path.Combine(Path.GetDirectoryName(source), "real-copy.png");

            TextureUtilities.CopyTexture(source, destination, 100, logger);

            Assert.Equal(png, File.ReadAllBytes(destination));
            Assert.Empty(logger.Warnings);
            Assert.Empty(TextureUtilities.Corrections);
        }

        [Fact]
        public void JpegBehindPngNameIsExportedAsJpgWithoutReEncoding()
        {
            var logger = new TestLogger();
            var jpeg = SyntheticImages.Encoded(4, 4, Color.Gray, ImageFormat.Jpeg);
            var source = SyntheticImages.WriteTemp("photo.png", jpeg);

            Assert.Equal("jpg", TextureUtilities.GetValidImageFormatForFile(source, logger));
            Assert.Single(logger.Warnings);
            Assert.Contains("content is JPG", logger.Warnings[0]);

            // GLB embedding as image/jpeg keeps the original bytes.
            Assert.Equal(jpeg, TextureUtilities.ReadImageBytes(source, "jpeg", 100, logger));

            // External-texture export with a .jpg destination copies verbatim too.
            var destination = Path.Combine(Path.GetDirectoryName(source), "photo.jpg");
            TextureUtilities.CopyTexture(source, destination, 100, logger);
            Assert.Equal(jpeg, File.ReadAllBytes(destination));
        }

        [Fact]
        public void ReadImageBytesReEncodesTgaAsPngForGlbEmbedding()
        {
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("embedded.png", SyntheticImages.Tga32(2, 2, Pixels, withFooter: false));

            var bytes = TextureUtilities.ReadImageBytes(source, "png", 100, logger);

            Assert.True(SyntheticImages.IsPng(bytes));
            using (var stream = new MemoryStream(bytes))
            using (var decoded = new Bitmap(stream))
            {
                Assert.Equal(Pixels[3].ToArgb(), decoded.GetPixel(1, 1).ToArgb());
            }
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public void ReadImageBytesReturnsGenuinePngVerbatim()
        {
            var logger = new TestLogger();
            var png = SyntheticImages.Encoded(2, 2, Color.Teal, ImageFormat.Png);
            var source = SyntheticImages.WriteTemp("verbatim.png", png);
            Assert.Equal(png, TextureUtilities.ReadImageBytes(source, "png", 100, logger));
        }

        [Fact]
        public void LoadTextureAndGetBitmapDecodeMislabelledFiles()
        {
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("mislabelled.png", SyntheticImages.Tga32(2, 2, Pixels, withFooter: true));
            using (var loaded = TextureUtilities.LoadTexture(source, logger))
            {
                Assert.NotNull(loaded);
                Assert.Equal(Pixels[0].ToArgb(), loaded.GetPixel(0, 0).ToArgb());
            }
            using (var bitmap = TextureUtilities.GetBitmap(source, TextureUtilities.NoTransforms, logger))
            {
                Assert.NotNull(bitmap);
                Assert.Equal(Pixels[2].ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
            }
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public void UnrecognisedBytesFallBackToTheExtension()
        {
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("garbage.png", System.Text.Encoding.ASCII.GetBytes("not an image at all, just text that is long enough"));
            string declared, actual;
            Assert.False(TextureUtilities.HasFormatMismatch(source, out declared, out actual));
            Assert.Equal("png", declared);
            Assert.Null(actual);
            Assert.Equal("png", TextureUtilities.GetSourceImageFormat(source));
            Assert.Equal("png", TextureUtilities.GetValidImageFormatForFile(source, logger));
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public void MissingFileFallsBackToTheExtensionWithoutWarning()
        {
            var logger = new TestLogger();
            var missing = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".jpeg");
            Assert.Equal("jpg", TextureUtilities.GetValidImageFormatForFile(missing, logger));
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public void BeginExportForgetsTheCorrectionLog()
        {
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("again.png", SyntheticImages.Tga32(1, 1, new[] { Color.Red }, withFooter: true));
            TextureUtilities.GetValidImageFormatForFile(source, logger);
            Assert.Single(TextureUtilities.Corrections);
            TextureUtilities.BeginExport();
            Assert.Empty(TextureUtilities.Corrections);
            TextureUtilities.GetValidImageFormatForFile(source, logger);
            Assert.Equal(2, logger.Warnings.Count); // reported again after the reset
        }

        /// <summary>
        /// Runs only when PLANNER_REAL_TGA points at a real mislabelled file (for example the TGA bytes extracted from
        /// an HS2 GLB image declared image/png). Proves the production bytes round-trip to a decodable PNG.
        /// </summary>
        [Fact]
        public void RealMislabelledFileRoundTrips()
        {
            var realFile = Environment.GetEnvironmentVariable("PLANNER_REAL_TGA");
            if (string.IsNullOrEmpty(realFile) || !File.Exists(realFile))
            {
                output.WriteLine("PLANNER_REAL_TGA not set; skipped.");
                return;
            }
            var logger = new TestLogger();
            var source = SyntheticImages.WriteTemp("ML_Weldlock_surface_DIFFUSE.png", File.ReadAllBytes(realFile));
            Assert.Equal("tga", TextureUtilities.GetSourceImageFormat(source));
            Assert.Equal("png", TextureUtilities.GetValidImageFormatForFile(source, logger));

            var bytes = TextureUtilities.ReadImageBytes(source, "png", 100, logger);
            Assert.True(SyntheticImages.IsPng(bytes));
            using (var stream = new MemoryStream(bytes))
            using (var decoded = new Bitmap(stream))
            {
                output.WriteLine("decoded {0}x{1} {2}, {3} PNG bytes from {4} source bytes", decoded.Width, decoded.Height, decoded.PixelFormat, bytes.Length, new FileInfo(source).Length);
                Assert.True(decoded.Width > 0 && decoded.Height > 0);
            }
            foreach (var warning in logger.Warnings) output.WriteLine(warning);
            Assert.Empty(logger.Errors);
        }
    }

    [CollectionDefinition("TextureUtilities static state", DisableParallelization = true)]
    public class TextureUtilitiesCollection
    {
    }
}
