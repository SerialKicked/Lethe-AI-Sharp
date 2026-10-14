using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LetheAISharp.Files.Tests
{
    /// <summary>
    /// Manual tests for <see cref="ImageUtils"/>. Call <see cref="RunAllTests"/> from a throwaway console project.
    /// Test images are generated in memory, no external files are needed.
    /// </summary>
    public static class ImageUtilsTest
    {
        /// <summary>
        /// Runs every test. Throws on the first failure.
        /// </summary>
        public static void RunAllTests()
        {
            TestGetScaledSize();
            TestPngOutputIsCleanRgb();
            TestAlphaIsFlattenedOnWhite();
            TestSemiTransparentIsBlended();
            TestScalingKeepsAspectRatio();
            TestNoUpscaling();
            TestExifOrientationIsApplied();
            TestAnimatedGifUsesFirstFrame();
            TestSupportedFormats();
            TestHighBitDepthAndPaletteAreNormalized();
            TestCmykIsConvertedToRgb();
            TestMetadataIsStripped();
            TestUnsupportedDataIsRejected();
            TestFileAndStreamOverloads();
            TestEncodeJpeg();
            Console.WriteLine("All ImageUtils tests passed.");
        }

        // ---------------------------------------------------------------- helpers

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("ImageUtilsTest failed: " + message);
        }

        private static byte[] Encode(MagickImage image, MagickFormat format)
        {
            image.Format = format;
            using var ms = new MemoryStream();
            image.Write(ms);
            return ms.ToArray();
        }

        private static MagickImage Solid(IMagickColor<byte> color, uint width, uint height)
            => new(color, width, height);

        private static MagickImage Decode(string base64)
            => new(Convert.FromBase64String(base64));

        private static (byte R, byte G, byte B) Pixel(MagickImage image, int x, int y)
        {
            using var pixels = image.GetPixels();
            var c = pixels.GetPixel(x, y).ToColor()!;
            return (c.R, c.G, c.B);
        }

        private static bool Near((byte R, byte G, byte B) p, byte r, byte g, byte b, int tolerance = 12)
            => Math.Abs(p.R - r) <= tolerance && Math.Abs(p.G - g) <= tolerance && Math.Abs(p.B - b) <= tolerance;

        /// <summary>
        /// Lists the chunk types of a PNG file, in order.
        /// </summary>
        private static List<string> PngChunks(byte[] png)
        {
            var list = new List<string>();
            int pos = 8;
            while (pos + 8 <= png.Length)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                list.Add(Encoding.ASCII.GetString(png, pos + 4, 4));
                pos += 12 + len;
            }
            return list;
        }

        // ---------------------------------------------------------------- tests

        /// <summary>
        /// GetScaledSize is pure math, check the documented rules.
        /// </summary>
        public static void TestGetScaledSize()
        {
            var s = ImageUtils.GetScaledSize(800, 600, 1024);
            Check(s.Width == 800 && s.Height == 600, "images within the limit must be untouched");

            s = ImageUtils.GetScaledSize(800, 600, 0);
            Check(s.Width == 800 && s.Height == 600, "maxSize 0 must disable scaling");

            s = ImageUtils.GetScaledSize(4000, 3000, 1000);
            Check(s.Width == 1000 && s.Height == 750, $"regular fit expected 1000x750, got {s.Width}x{s.Height}");

            s = ImageUtils.GetScaledSize(3000, 4000, 1000);
            Check(s.Width == 750 && s.Height == 1000, $"portrait fit expected 750x1000, got {s.Width}x{s.Height}");

            // Extreme aspect ratio: long side may exceed the limit, but never more than 4x, and the short side must be larger than with a plain fit.
            s = ImageUtils.GetScaledSize(8000, 400, 1024);
            Check(s.Width > 1024 && s.Width <= 4096, $"extreme ratio long side out of range: {s.Width}");
            Check(s.Height > 400 * 1024 / 8000, $"extreme ratio short side was crushed: {s.Height}");
            Check(s.Width <= 8000 && s.Height <= 400, "extreme ratio must never upscale");

            s = ImageUtils.GetScaledSize(0, 0, 1024);
            Check(s.Width == 0 && s.Height == 0, "degenerate sizes must pass through");
            Console.WriteLine("OK GetScaledSize rules");
        }

        /// <summary>
        /// The output must be a plain 8-bit RGB, non interlaced PNG made only of critical chunks.
        /// </summary>
        public static void TestPngOutputIsCleanRgb()
        {
            using var src = Solid(MagickColors.Red, 64, 48);
            var b64 = ImageUtils.ImageToBase64(Encode(src, MagickFormat.Png), 0);
            Check(b64 != null, "conversion returned null");
            var png = Convert.FromBase64String(b64!);

            Check(png.Length > 33 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "output is not a PNG");
            Check(png[24] == 8, $"bit depth must be 8, got {png[24]}");
            Check(png[25] == 2, $"color type must be 2 (RGB), got {png[25]}");
            Check(png[28] == 0, $"interlace method must be 0, got {png[28]}");
            Console.WriteLine("OK PNG output is 8-bit RGB, no interlace");
        }

        /// <summary>
        /// Fully transparent pixels must show up white, not as their raw (black) color.
        /// </summary>
        public static void TestAlphaIsFlattenedOnWhite()
        {
            using var src = Solid(MagickColors.Transparent, 32, 32);
            var b64 = ImageUtils.ImageToBase64(Encode(src, MagickFormat.Png32), 0);
            Check(b64 != null, "conversion returned null");
            using var res = Decode(b64!);
            Check(!res.HasAlpha, "result must not have an alpha channel");
            Check(Near(Pixel(res, 5, 5), 255, 255, 255), "transparent pixel must be white");
            Console.WriteLine("OK transparency flattened on white");
        }

        /// <summary>
        /// Half transparent black over white must come out mid gray, i.e. actually blended.
        /// </summary>
        public static void TestSemiTransparentIsBlended()
        {
            using var src = Solid(new MagickColor(0, 0, 0, 128), 16, 16);
            var b64 = ImageUtils.ImageToBase64(Encode(src, MagickFormat.Png32), 0);
            Check(b64 != null, "conversion returned null");
            using var res = Decode(b64!);
            var p = Pixel(res, 4, 4);
            Check(p.R > 100 && p.R < 156 && Near(p, p.R, p.R, p.R, 2), $"semi transparent black should blend to mid gray, got {p}");
            Console.WriteLine("OK semi transparency blended");
        }

        /// <summary>
        /// Large images are scaled to the limit with their aspect ratio preserved.
        /// </summary>
        public static void TestScalingKeepsAspectRatio()
        {
            using var src = Solid(MagickColors.Blue, 3000, 2000);
            var b64 = ImageUtils.ImageToBase64(Encode(src, MagickFormat.Png), 512);
            Check(b64 != null, "conversion returned null");
            using var res = Decode(b64!);
            Check(res.Width == 512 && res.Height == 341, $"expected 512x341, got {res.Width}x{res.Height}");
            Check(Near(Pixel(res, 100, 100), 0, 0, 255), "scaled content color changed");
            Console.WriteLine("OK scaling keeps aspect ratio");
        }

        /// <summary>
        /// Small images must never be upscaled.
        /// </summary>
        public static void TestNoUpscaling()
        {
            using var src = Solid(MagickColors.Green, 100, 80);
            var b64 = ImageUtils.ImageToBase64(Encode(src, MagickFormat.Png), 1024);
            Check(b64 != null, "conversion returned null");
            using var res = Decode(b64!);
            Check(res.Width == 100 && res.Height == 80, $"image must keep its size, got {res.Width}x{res.Height}");
            Console.WriteLine("OK no upscaling");
        }

        /// <summary>
        /// EXIF orientation must be baked into the pixels (orientation 6 swaps width and height).
        /// </summary>
        public static void TestExifOrientationIsApplied()
        {
            // Left half red, right half blue, stored 100x50 with orientation 6 (rotate 90 clockwise to display).
            using var left = Solid(MagickColors.Red, 50, 50);
            using var right = Solid(MagickColors.Blue, 50, 50);
            using var src = new MagickImage(MagickColors.Black, 100, 50);
            src.Composite(left, 0, 0);
            src.Composite(right, 50, 0);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            src.SetProfile(exif);
            src.Orientation = Orientation.RightTop;

            var jpeg = Encode(src, MagickFormat.Jpeg);
            using (var check = new MagickImage(jpeg))
                Check(check.Orientation == Orientation.RightTop, $"test setup: source should carry orientation 6, got {check.Orientation}");
            var b64 = ImageUtils.ImageToBase64(jpeg, 0);
            Check(b64 != null, "conversion returned null");
            using var res = Decode(b64!);
            Check(res.Width == 50 && res.Height == 100, $"orientation 6 must give 50x100, got {res.Width}x{res.Height}");
            // Rotated 90 degrees clockwise: the red (left) half ends up on top, the blue (right) half at the bottom.
            Check(Near(Pixel(res, 25, 10), 255, 0, 0, 40), "top half should be red after rotation");
            Check(Near(Pixel(res, 25, 90), 0, 0, 255, 40), "bottom half should be blue after rotation");
            Console.WriteLine("OK EXIF orientation applied");
        }

        /// <summary>
        /// Animated images are reduced to their first frame.
        /// </summary>
        public static void TestAnimatedGifUsesFirstFrame()
        {
            foreach (var format in new[] { MagickFormat.Gif, MagickFormat.WebP })
            {
                using var frames = new MagickImageCollection();
                frames.Add(Solid(MagickColors.Red, 40, 40));
                frames.Add(Solid(MagickColors.Blue, 40, 40));
                frames.Add(Solid(MagickColors.Green, 40, 40));
                foreach (var f in frames) f.AnimationDelay = 10;
                using var ms = new MemoryStream();
                frames.Write(ms, format);

                using (var source = new MagickImageCollection(ms.ToArray()))
                    Check(source.Count == 3, $"test setup: animated {format} should have 3 frames, got {source.Count}");

                var b64 = ImageUtils.ImageToBase64(ms.ToArray(), 0);
                Check(b64 != null, $"{format}: conversion returned null");
                using var res = Decode(b64!);
                Check(res.Width == 40 && res.Height == 40, $"{format}: wrong size");
                Check(Near(Pixel(res, 20, 20), 255, 0, 0, 20), $"{format}: first frame (red) expected");
            }
            Console.WriteLine("OK animated GIF and WebP -> first frame");
        }

        /// <summary>
        /// Every format the apps accept must decode.
        /// </summary>
        public static void TestSupportedFormats()
        {
            foreach (var format in new[] { MagickFormat.Jpeg, MagickFormat.Png, MagickFormat.Gif, MagickFormat.Bmp, MagickFormat.WebP, MagickFormat.Tiff })
            {
                using var src = Solid(MagickColors.Red, 48, 32);
                var data = Encode(src, format);
                var b64 = ImageUtils.ImageToBase64(data, 0);
                Check(b64 != null, $"{format} could not be converted");
                using var res = Decode(b64!);
                Check(res.Width == 48 && res.Height == 32, $"{format}: wrong size {res.Width}x{res.Height}");
                Check(Near(Pixel(res, 10, 10), 255, 0, 0, 25), $"{format}: wrong color");
            }
            Console.WriteLine("OK JPEG, PNG, GIF, BMP, WebP and TIFF decode");
        }

        /// <summary>
        /// 16-bit and palettized sources must still produce 8-bit RGB.
        /// </summary>
        public static void TestHighBitDepthAndPaletteAreNormalized()
        {
            using var deep = Solid(MagickColors.Orange, 32, 32);
            deep.Depth = 16;
            var deepPng = Encode(deep, MagickFormat.Png48);
            Check(Convert.FromBase64String(Convert.ToBase64String(deepPng))[24] == 16, "test setup: source should be 16-bit");
            var b64 = ImageUtils.ImageToBase64(deepPng, 0);
            Check(b64 != null, "16-bit conversion returned null");
            var png = Convert.FromBase64String(b64!);
            Check(png[24] == 8 && png[25] == 2, $"16-bit source must become 8-bit RGB, got depth {png[24]} type {png[25]}");

            using var pal = Solid(MagickColors.Purple, 32, 32);
            var palPng = Encode(pal, MagickFormat.Png8);
            Check(palPng[25] == 3, $"test setup: source should be palettized, got color type {palPng[25]}");
            b64 = ImageUtils.ImageToBase64(palPng, 0);
            Check(b64 != null, "palette conversion returned null");
            png = Convert.FromBase64String(b64!);
            Check(png[24] == 8 && png[25] == 2, $"palette source must become 8-bit RGB, got depth {png[24]} type {png[25]}");
            Console.WriteLine("OK 16-bit and palette sources normalized");
        }

        /// <summary>
        /// CMYK JPEG must come out as RGB with approximately the right color.
        /// </summary>
        public static void TestCmykIsConvertedToRgb()
        {
            using var src = Solid(MagickColors.Red, 32, 32);
            src.ColorSpace = ColorSpace.CMYK;
            var jpeg = Encode(src, MagickFormat.Jpeg);
            using (var check = new MagickImage(jpeg))
                Check(check.ColorSpace == ColorSpace.CMYK, "test setup: source should be CMYK");

            var b64 = ImageUtils.ImageToBase64(jpeg, 0);
            Check(b64 != null, "conversion returned null");
            var png = Convert.FromBase64String(b64!);
            Check(png[25] == 2, "result must be RGB");
            using var res = Decode(b64!);
            var p = Pixel(res, 10, 10);
            Check(p.R > 180 && p.G < 90 && p.B < 90, $"CMYK red should stay red, got {p}");
            Console.WriteLine("OK CMYK converted to RGB");
        }

        /// <summary>
        /// Nothing but critical chunks may remain in the output (no text, EXIF, ICC, gamma, tRNS...).
        /// </summary>
        public static void TestMetadataIsStripped()
        {
            using var src = Solid(MagickColors.Red, 32, 32);
            src.SetAttribute("comment", "secret comment");
            src.SetAttribute("png:text", "hello");
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "UnitTest");
            src.SetProfile(exif);
            src.SetProfile(ColorProfiles.SRGB);
            var data = Encode(src, MagickFormat.Png);
            Check(PngChunks(data).Count > 3, "test setup: source should carry extra chunks");

            var b64 = ImageUtils.ImageToBase64(data, 0);
            Check(b64 != null, "conversion returned null");
            var chunks = PngChunks(Convert.FromBase64String(b64!));
            foreach (var c in chunks)
                Check(c is "IHDR" or "IDAT" or "IEND", $"unexpected chunk in output: {c} (all: {string.Join(",", chunks)})");
            Console.WriteLine("OK metadata stripped");
        }

        /// <summary>
        /// Anything that is not one of the supported raster formats must be refused, whatever its name or content claims.
        /// </summary>
        public static void TestUnsupportedDataIsRejected()
        {
            Check(ImageUtils.ImageToBase64(Encoding.UTF8.GetBytes("hello world"), 0) == null, "plain text must be rejected");
            Check(ImageUtils.ImageToBase64(Array.Empty<byte>(), 0) == null, "empty data must be rejected");

            var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10'/></svg>";
            Check(ImageUtils.ImageToBase64(Encoding.UTF8.GetBytes(svg), 0) == null, "SVG must be rejected");

            var msl = "<?xml version=\"1.0\"?><image><read filename=\"x.png\"/></image>";
            Check(ImageUtils.ImageToBase64(Encoding.UTF8.GetBytes(msl), 0) == null, "MSL script must be rejected");

            // Truncated PNG: valid signature, garbage afterwards.
            var bad = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
            Check(ImageUtils.ImageToBase64(bad, 0) == null, "corrupt PNG must be rejected");

            Check(ImageUtils.ImageToBase64(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".png"), 0) == null, "missing file must return null");
            Console.WriteLine("OK unsupported or corrupt data rejected");
        }

        /// <summary>
        /// The path and stream overloads must give the same result as the byte overload, and the path overload must honor its cache invalidation.
        /// </summary>
        public static void TestFileAndStreamOverloads()
        {
            using var src = Solid(MagickColors.Red, 64, 64);
            var data = Encode(src, MagickFormat.Png);
            var fromBytes = ImageUtils.ImageToBase64(data, 32);
            Check(fromBytes != null, "byte overload failed");

            using (var ms = new MemoryStream(data))
                Check(ImageUtils.ImageToBase64(ms, 32) == fromBytes, "stream overload differs from byte overload");

            // A file named .txt must still be decoded from its content, not its name.
            var path = Path.Combine(Path.GetTempPath(), "imgutils-" + Guid.NewGuid() + ".txt");
            try
            {
                File.WriteAllBytes(path, data);
                Check(ImageUtils.ImageToBase64(path, 32) == fromBytes, "path overload differs from byte overload");
                // Second call is served from the cache and must be identical.
                Check(ImageUtils.ImageToBase64(path, 32) == fromBytes, "cached result differs");

                // Rewrite the file with different content: the cache must be invalidated.
                using var other = Solid(MagickColors.Blue, 64, 64);
                File.WriteAllBytes(path, Encode(other, MagickFormat.Png));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
                var changed = ImageUtils.ImageToBase64(path, 32);
                Check(changed != null && changed != fromBytes, "cache was not invalidated after the file changed");
            }
            finally
            {
                File.Delete(path);
            }
            Console.WriteLine("OK path/stream/byte overloads consistent, cache invalidation works");
        }

        /// <summary>
        /// JPEG encoding for thumbnails: valid JPEG, scaled, transparency flattened to white.
        /// </summary>
        public static void TestEncodeJpeg()
        {
            using var src = Solid(MagickColors.Transparent, 400, 200);
            var path = Path.Combine(Path.GetTempPath(), "imgutils-" + Guid.NewGuid() + ".png");
            try
            {
                File.WriteAllBytes(path, Encode(src, MagickFormat.Png32));
                var jpeg = ImageUtils.EncodeJpeg(path, 128, 70);
                Check(jpeg != null, "EncodeJpeg returned null");
                Check(jpeg![0] == 0xFF && jpeg[1] == 0xD8 && jpeg[2] == 0xFF, "output is not a JPEG");
                using var res = new MagickImage(jpeg);
                Check(res.Format == MagickFormat.Jpeg, "decoded format is not JPEG");
                Check(res.Width == 128 && res.Height == 64, $"expected 128x64, got {res.Width}x{res.Height}");
                Check(Near(Pixel(res, 10, 10), 255, 255, 255), "transparent area must be white in the JPEG");
            }
            finally
            {
                File.Delete(path);
            }
            Check(ImageUtils.EncodeJpeg(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".png"), 128) == null, "missing file must return null");
            Console.WriteLine("OK EncodeJpeg");
        }
    }
}
