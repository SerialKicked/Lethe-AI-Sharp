using ImageMagick;
using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace LetheAISharp
{
    /// <summary>
    /// Image helpers used to prepare pictures for vision-capable backends and for UI thumbnails.
    /// The public API is deliberately independent of the underlying imaging library: it only deals with
    /// file paths, streams, byte arrays and base64 strings.
    /// <para>
    /// Only JPEG, PNG, GIF, BMP, WebP and TIFF files are decoded. The format is detected from the file
    /// signature (never from the file name), which keeps the many exotic and script-capable coders of
    /// the underlying library out of reach of untrusted input.
    /// </para>
    /// </summary>
    public static class ImageUtils
    {
        /// <summary>
        /// When an image has an extreme aspect ratio, fitting its long side to the size limit would crush the
        /// short side into an unreadable sliver. The short side is therefore allowed to keep at least
        /// <c>maxSize / MinShortSideDivisor</c> pixels, as long as the pixel budget and the long side limit allow it.
        /// </summary>
        private const int MinShortSideDivisor = 4;

        /// <summary>
        /// Hard limit for the long side of an image, as a multiple of the size limit. Only reachable by images
        /// with an extreme aspect ratio (see <see cref="MinShortSideDivisor"/>).
        /// </summary>
        private const int MaxLongSideFactor = 4;

        /// <summary>
        /// Computes the size an image should be scaled to so that it respects <paramref name="maxSize"/>, without
        /// ever upscaling and while preserving the aspect ratio.
        /// <para>
        /// Regular images are fitted so that neither dimension exceeds <paramref name="maxSize"/>. Images with an extreme
        /// aspect ratio (wider/taller than roughly 4:1) are allowed to exceed <paramref name="maxSize"/> on their long side so that the short
        /// side keeps at least <c>maxSize / 4</c> pixels, bounded by a total pixel budget of <c>maxSize * maxSize</c>
        /// and a long side of at most <c>maxSize * 4</c>.
        /// </para>
        /// </summary>
        /// <param name="width">Source width in pixels</param>
        /// <param name="height">Source height in pixels</param>
        /// <param name="maxSize">Maximum size of the long side for regular images. Zero or less disables scaling.</param>
        /// <returns>The target size. Identical to the source size if no scaling is needed.</returns>
        public static Size GetScaledSize(int width, int height, int maxSize)
        {
            if (maxSize <= 0 || width <= 0 || height <= 0 || (width <= maxSize && height <= maxSize))
                return new Size(width, height);

            double longSide = Math.Max(width, height);
            double shortSide = Math.Min(width, height);

            // Regular case: fit the long side to the limit.
            double scale = maxSize / longSide;

            // Extreme aspect ratio: give the short side some room, within the pixel budget.
            double minShortSide = Math.Max(1, maxSize / MinShortSideDivisor);
            if (shortSide * scale < minShortSide)
            {
                double boosted = minShortSide / shortSide;
                double pixelBudgetScale = Math.Sqrt((double)maxSize * maxSize / ((double)width * height));
                double longSideScale = (double)maxSize * MaxLongSideFactor / longSide;
                boosted = Math.Min(boosted, Math.Min(pixelBudgetScale, longSideScale));
                scale = Math.Max(scale, boosted);
            }

            // Never upscale.
            scale = Math.Min(scale, 1.0);

            int newWidth = Math.Max(1, (int)Math.Round(width * scale));
            int newHeight = Math.Max(1, (int)Math.Round(height * scale));
            return new Size(newWidth, newHeight);
        }

        /// <summary>
        /// Memoized encodes, keyed by path + maxSize and invalidated by the file's last-write time.
        /// Decode + resize + PNG encode is by far the most expensive part of rebuilding a chat request,
        /// and prompt building can rebuild the same images several times per generation (token-count
        /// trim iterations, rerolls). Entries are large strings, so the cache is hard-capped.
        /// </summary>
        private static readonly ConcurrentDictionary<string, (DateTime WriteTimeUtc, string Data)> _base64Cache = new(StringComparer.OrdinalIgnoreCase);
        private const int Base64CacheCap = 12;

        /// <summary>
        /// Converts an image file to a base64 encoded PNG (always 8-bit RGB, single frame, no metadata)
        /// suitable for vision-capable backends. The image is scaled down if needed, see <see cref="GetScaledSize"/>.
        /// Animated images are reduced to their first frame, EXIF orientation is applied to the pixels,
        /// and transparency is flattened onto a white background.
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="maxSize">Optional maximum size for width or height. If specified, image will be scaled down proportionally if needed.</param>
        /// <returns>Base64 encoded string of the PNG data, or null on failure</returns>
        public static string? ImageToBase64(string imagePath, int maxSize = 0)
        {
            try
            {
                var key = $"{maxSize}|{imagePath}";
                var writeTime = File.GetLastWriteTimeUtc(imagePath);
                if (_base64Cache.TryGetValue(key, out var cached) && cached.WriteTimeUtc == writeTime)
                    return cached.Data;

                var res = ImageToBase64(File.ReadAllBytes(imagePath), maxSize);
                if (res != null)
                {
                    // Crude but effective bound: entries are big and the active working set is small
                    // (a handful of images per prompt), so clearing on overflow costs at most one
                    // re-encode per image.
                    if (_base64Cache.Count >= Base64CacheCap)
                        _base64Cache.Clear();
                    _base64Cache[key] = (writeTime, res);
                }
                return res;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error converting image to base64: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Converts in-memory image data to a base64 encoded PNG (always 8-bit RGB, single frame, no metadata),
        /// with optional scaling. Same behavior as <see cref="ImageToBase64(string, int)"/>.
        /// </summary>
        /// <param name="data">The encoded image file content (JPEG, PNG, GIF, BMP, WebP or TIFF)</param>
        /// <param name="maxSize">Optional maximum size for width or height. If specified, image will be scaled down proportionally if needed.</param>
        /// <returns>Base64 encoded string of the PNG data, or null on failure</returns>
        public static string? ImageToBase64(byte[] data, int maxSize = 0)
        {
            try
            {
                using var image = Load(data);
                Prepare(image, maxSize);

                // Be explicit about every encoding parameter. Left to its defaults, the encoder inherits the
                // color type, bit depth, palette and interlacing of the source image, which can produce
                // palettized, 1/2/4 bit or 16 bit PNGs that some decoders (stb_image/mtmd, PIL) handle poorly.
                // Png24 is 8-bit RGB without alpha.
                image.Format = MagickFormat.Png24;
                image.Depth = 8;
                image.Settings.Interlace = Interlace.NoInterlace;
                image.Settings.SetDefine("png:exclude-chunk", "all");

                using MemoryStream ms = new();
                image.Write(ms);
                return Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error converting image to base64: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Converts an image stream to a base64 encoded PNG (always 8-bit RGB, single frame, no metadata),
        /// with optional scaling. Same behavior as <see cref="ImageToBase64(string, int)"/>.
        /// The stream is read from its current position to its end and is not closed.
        /// </summary>
        /// <param name="stream">Stream containing the encoded image file (JPEG, PNG, GIF, BMP, WebP or TIFF)</param>
        /// <param name="maxSize">Optional maximum size for width or height. If specified, image will be scaled down proportionally if needed.</param>
        /// <returns>Base64 encoded string of the PNG data, or null on failure</returns>
        public static string? ImageToBase64(Stream stream, int maxSize = 0)
        {
            try
            {
                using MemoryStream ms = new();
                stream.CopyTo(ms);
                return ImageToBase64(ms.ToArray(), maxSize);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error converting image to base64: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Loads an image file, scales it down if needed (see <see cref="GetScaledSize"/>) and encodes it as a JPEG.
        /// Meant for thumbnails and previews. Animated images are reduced to their first frame, EXIF orientation
        /// is applied to the pixels, transparency is flattened onto a white background (JPEG has no alpha)
        /// and metadata is removed.
        /// </summary>
        /// <param name="imagePath">Path to the image file (JPEG, PNG, GIF, BMP, WebP or TIFF)</param>
        /// <param name="maxSize">Maximum width or height. Zero or less disables scaling.</param>
        /// <param name="quality">JPEG quality, 1 to 100</param>
        /// <returns>The JPEG file content, or null on failure</returns>
        public static byte[]? EncodeJpeg(string imagePath, int maxSize, int quality = 85)
        {
            try
            {
                using var image = Load(File.ReadAllBytes(imagePath));
                Prepare(image, maxSize);

                image.Format = MagickFormat.Jpeg;
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                using MemoryStream ms = new();
                image.Write(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error encoding image to JPEG: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Loads an image from a URL and converts it to a base64 encoded PNG string
        /// </summary>
        /// <param name="imageUrl">URL of the image</param>
        /// <returns>Base64 encoded string of the image data</returns>
        public static async Task<string?> ImageFromUrlToBase64Async(string imageUrl)
        {
            try
            {
                using HttpClient httpClient = new();
                byte[] imageBytes = await httpClient.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                return ImageToBase64(imageBytes);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error downloading or converting image from URL to base64: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Decodes the first frame of an image from its encoded bytes. The format is detected from the file
        /// signature and forced on the decoder, so only the supported formats can ever be decoded.
        /// </summary>
        /// <exception cref="NotSupportedException">The data is not a supported image format</exception>
        private static MagickImage Load(byte[] data)
        {
            var format = DetectFormat(data) ?? throw new NotSupportedException("Unsupported or unrecognized image format.");

            // Only the first frame is ever used, so don't waste time decoding the others.
            var settings = new MagickReadSettings
            {
                Format = format,
                FrameIndex = 0,
                FrameCount = 1
            };
            return new MagickImage(data, settings);
        }

        /// <summary>
        /// Detects a supported image format from its file signature.
        /// </summary>
        private static MagickFormat? DetectFormat(ReadOnlySpan<byte> h)
        {
            // JPEG: FF D8 FF
            if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
                return MagickFormat.Jpeg;
            // PNG: 89 'PNG' 0D 0A 1A 0A
            if (h.Length >= 8 && h[0] == 0x89 && h[1] == 'P' && h[2] == 'N' && h[3] == 'G'
                && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A)
                return MagickFormat.Png;
            // GIF: 'GIF87a' / 'GIF89a'
            if (h.Length >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8' && (h[4] == '7' || h[4] == '9') && h[5] == 'a')
                return MagickFormat.Gif;
            // WebP: 'RIFF' size 'WEBP'
            if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
                && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
                return MagickFormat.WebP;
            // TIFF / BigTIFF: 'II' 2A 00 | 'MM' 00 2A | 'II' 2B 00 | 'MM' 00 2B
            if (h.Length >= 4 && ((h[0] == 'I' && h[1] == 'I' && (h[2] == 0x2A || h[2] == 0x2B) && h[3] == 0x00)
                || (h[0] == 'M' && h[1] == 'M' && h[2] == 0x00 && (h[3] == 0x2A || h[3] == 0x2B))))
                return MagickFormat.Tiff;
            // BMP: 'BM'
            if (h.Length >= 2 && h[0] == 'B' && h[1] == 'M')
                return MagickFormat.Bmp;
            return null;
        }

        /// <summary>
        /// Brings a freshly decoded image to a clean, encoder-independent state: EXIF orientation applied to the
        /// pixels, converted to sRGB, scaled to <paramref name="maxSize"/>, transparency flattened onto white
        /// and all metadata removed. Modifies <paramref name="image"/> in place.
        /// </summary>
        private static void Prepare(MagickImage image, int maxSize)
        {
            // The EXIF orientation is dropped when encoding, so it has to be baked into the pixels.
            // This also swaps the dimensions for the orientations that require it.
            image.AutoOrient();

            // CMYK (typically JPEG/TIFF) and other exotic colorspaces must end up as sRGB.
            if (image.ColorSpace != ColorSpace.sRGB && image.ColorSpace != ColorSpace.Gray)
                image.TransformColorSpace(ColorProfiles.SRGB);

            var target = GetScaledSize((int)image.Width, (int)image.Height, maxSize);
            if (target.Width != (int)image.Width || target.Height != (int)image.Height)
            {
                // Catmull-Rom is the classic "bicubic" filter.
                image.FilterType = FilterType.Catrom;
                image.Resize(new MagickGeometry((uint)target.Width, (uint)target.Height) { IgnoreAspectRatio = true });
            }

            // Alpha is dropped when encoding to RGB/JPEG. Without flattening, transparent pixels would show their raw
            // (often black) color values to the model instead of what a viewer displays over a light background.
            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }

            // Make sure nothing from the source (profiles, comments, color key...) leaks into the encoder.
            image.Strip();

            // Strip leaves the orientation property alone, and the PNG encoder would write it out in a private chunk.
            // The orientation has already been applied to the pixels.
            image.Orientation = Orientation.Undefined;
        }
    }
}
