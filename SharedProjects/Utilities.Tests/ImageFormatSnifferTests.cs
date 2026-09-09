using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Utilities;
using Xunit;

namespace Utilities.Tests
{
    public class ImageFormatSnifferTests
    {
        [Fact]
        public void RecognisesPngJpegGifBmpTiffByMagicBytes()
        {
            Assert.Equal("png", ImageFormatSniffer.Detect(SyntheticImages.Encoded(2, 2, Color.Red, ImageFormat.Png)));
            Assert.Equal("jpg", ImageFormatSniffer.Detect(SyntheticImages.Encoded(2, 2, Color.Red, ImageFormat.Jpeg)));
            Assert.Equal("gif", ImageFormatSniffer.Detect(SyntheticImages.Encoded(2, 2, Color.Red, ImageFormat.Gif)));
            Assert.Equal("bmp", ImageFormatSniffer.Detect(SyntheticImages.Encoded(2, 2, Color.Red, ImageFormat.Bmp)));
            Assert.Equal("tif", ImageFormatSniffer.Detect(SyntheticImages.Encoded(2, 2, Color.Red, ImageFormat.Tiff)));
        }

        [Fact]
        public void RecognisesOtherSignatures()
        {
            Assert.Equal("dds", ImageFormatSniffer.Detect(Pad(Encoding.ASCII.GetBytes("DDS |xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"))));
            Assert.Equal("webp", ImageFormatSniffer.Detect(Pad(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 xxxxxxxxxxxxxxxxxxxx"))));
            Assert.Equal("psd", ImageFormatSniffer.Detect(Pad(Encoding.ASCII.GetBytes("8BPS\0xxxxxxxxxxxxxxxxxxxxxxxxxxxx"))));
            Assert.Equal("hdr", ImageFormatSniffer.Detect(Pad(Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n"))));
            Assert.Equal("ktx", ImageFormatSniffer.Detect(Pad(new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A })));
        }

        [Fact]
        public void RecognisesTgaByFooter()
        {
            var tga = SyntheticImages.Tga32(2, 2, new[] { Color.Red, Color.Green, Color.Blue, Color.White }, withFooter: true);
            Assert.Equal("tga", ImageFormatSniffer.Detect(tga));
        }

        [Fact]
        public void RecognisesTgaByHeaderWhenThereIsNoFooter()
        {
            var uncompressed = SyntheticImages.Tga32(3, 2, new Color[6], withFooter: false);
            Assert.Equal("tga", ImageFormatSniffer.Detect(uncompressed));
            Assert.Equal("tga", ImageFormatSniffer.Detect(SyntheticImages.TgaRleHeaderOnly(512, 512)));
        }

        [Fact]
        public void RejectsAnUncompressedTgaHeaderWhoseFileIsTooShort()
        {
            var header = SyntheticImages.Tga32(64, 64, new Color[64 * 64], withFooter: false);
            var truncated = new byte[100];
            System.Array.Copy(header, truncated, truncated.Length);
            Assert.Null(ImageFormatSniffer.Detect(truncated));
        }

        [Fact]
        public void DoesNotMistakeTextOrEmptyDataForAnImage()
        {
            Assert.Null(ImageFormatSniffer.Detect(Encoding.ASCII.GetBytes("Hello, this is definitely not a texture file at all.")));
            Assert.Null(ImageFormatSniffer.Detect(new byte[0]));
            Assert.Null(ImageFormatSniffer.Detect(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }));
            Assert.Null(ImageFormatSniffer.Detect((byte[])null));
        }

        [Fact]
        public void ReadsFromDiskAndReturnsNullForMissingFiles()
        {
            var path = SyntheticImages.WriteTemp("tga-in-disguise.png", SyntheticImages.Tga32(2, 2, new Color[4], withFooter: true));
            Assert.Equal("tga", ImageFormatSniffer.Detect(path));
            Assert.Null(ImageFormatSniffer.Detect(path + ".missing"));
        }

        [Fact]
        public void NormalisesExtensionSpellings()
        {
            Assert.Equal("jpg", ImageFormatSniffer.NormaliseToken(".JPEG"));
            Assert.Equal("jpg", ImageFormatSniffer.NormaliseToken("jpg"));
            Assert.Equal("tif", ImageFormatSniffer.NormaliseToken(".tiff"));
            Assert.Equal("png", ImageFormatSniffer.NormaliseToken(" .PNG "));
            Assert.Equal(string.Empty, ImageFormatSniffer.NormaliseToken(null));
        }

        private static byte[] Pad(byte[] bytes)
        {
            var padded = new byte[System.Math.Max(bytes.Length, 64)];
            System.Array.Copy(bytes, padded, bytes.Length);
            return padded;
        }
    }
}
