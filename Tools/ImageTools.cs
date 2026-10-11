using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace LetheAISharp
{
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
        /// Scales an image proportionally to respect the specified maximum size (see <see cref="GetScaledSize"/>).
        /// </summary>
        /// <param name="image">The source image to scale</param>
        /// <param name="maxSize">The maximum width or height allowed</param>
        /// <returns>A new scaled image if resizing was needed, or the original image if already within limits</returns>
        public static Image ScaleImage(Image image, int maxSize)
        {
            var target = GetScaledSize(image.Width, image.Height, maxSize);
            if (target.Width == image.Width && target.Height == image.Height)
                return image;

            // Clone and resize the image with high quality bicubic resampling
            return image.Clone(ctx => ctx.Resize(target.Width, target.Height, KnownResamplers.Bicubic));
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
        /// Animated images are reduced to their first frame, and transparency is flattened onto a white background.
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

                // Only the first frame is ever used, so don't waste time decoding the others.
                using var image = Image.Load(new DecoderOptions { MaxFrames = 1 }, imagePath);
                var res = ImageToBase64(image, maxSize);
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
                LLMEngine.Logger?.LogError(ex,"Error converting image to base64: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Converts an in-memory image to a base64 encoded PNG (always 8-bit RGB, single frame, no metadata),
        /// with optional scaling. The source image is never modified.
        /// Animated images are reduced to their first frame, EXIF orientation is applied to the pixels,
        /// and transparency is flattened onto a white background.
        /// </summary>
        /// <param name="image">The image to convert</param>
        /// <param name="maxSize">Optional maximum size for width or height. If specified, image will be scaled down proportionally if needed.</param>
        /// <returns>Base64 encoded string of the PNG data, or null on failure</returns>
        public static string? ImageToBase64(Image image, int maxSize = 0)
        {
            try
            {
                using var prepared = PrepareForEncoding(image, maxSize);

                // Be explicit about every encoding parameter. Left to its defaults, the PNG encoder inherits the
                // color type, bit depth, palette, interlacing and metadata of the source image, which can produce
                // palettized, 1/2/4 bit or 16 bit PNGs that some decoders (stb_image/mtmd, PIL) handle poorly.
                var encoder = new PngEncoder
                {
                    ColorType = PngColorType.Rgb,
                    BitDepth = PngBitDepth.Bit8,
                    InterlaceMethod = PngInterlaceMode.None,
                    SkipMetadata = true
                };

                using MemoryStream ms = new();
                prepared.Save(ms, encoder);
                return Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error converting image to base64: {mess}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Builds a clean, caller-independent copy of <paramref name="source"/> that is ready to be encoded:
        /// first frame only, EXIF orientation applied, scaled to <paramref name="maxSize"/>, transparency
        /// flattened onto white and non-pixel metadata removed.
        /// </summary>
        /// <returns>A new image the caller must dispose. <paramref name="source"/> is never modified.</returns>
        private static Image PrepareForEncoding(Image source, int maxSize)
        {
            // Animated sources: keep only the first frame (CloneFrame already returns an independent copy).
            Image current = source.Frames.Count > 1 ? source.Frames.CloneFrame(0) : source;
            try
            {
                int width = current.Width;
                int height = current.Height;

                // The EXIF orientation is dropped when encoding, so it has to be baked into the pixels.
                // Orientations 5 to 8 are the ones that swap the width and height.
                ushort orientation = GetExifOrientation(current);
                if (orientation >= 5 && orientation <= 8)
                    (width, height) = (height, width);

                var target = GetScaledSize(width, height, maxSize);
                bool resize = target.Width != width || target.Height != height;

                // Alpha is dropped when encoding to RGB. Without flattening, transparent pixels would show their raw
                // (often black) color values to the model instead of what a viewer displays over a light background.
                bool flatten = HasAlpha(current);

                Image result = current.Clone(ctx =>
                {
                    if (orientation > 1)
                        ctx.AutoOrient();
                    if (resize)
                        ctx.Resize(target.Width, target.Height, KnownResamplers.Bicubic);
                    if (flatten)
                        ctx.BackgroundColor(Color.White);
                });

                // Make sure nothing from the source leaks into the encoder (a PNG color key would end up as a tRNS chunk).
                result.Metadata.ExifProfile = null;
                result.Metadata.IccProfile = null;
                result.Metadata.IptcProfile = null;
                result.Metadata.XmpProfile = null;
                result.Metadata.GetPngMetadata().TransparentColor = null;
                return result;
            }
            finally
            {
                if (!ReferenceEquals(current, source))
                    current.Dispose();
            }
        }

        private static ushort GetExifOrientation(Image image)
        {
            var exif = image.Metadata.ExifProfile;
            if (exif != null && exif.TryGetValue(ExifTag.Orientation, out var value) && value is not null)
                return value.Value;
            return 1;
        }

        private static bool HasAlpha(Image image)
        {
            // Unknown alpha representation is treated as "has alpha" to stay on the safe side.
            return image.PixelType.AlphaRepresentation != PixelAlphaRepresentation.None;
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
                using MemoryStream ms = new(imageBytes);
                using Image image = Image.Load(new DecoderOptions { MaxFrames = 1 }, ms);
                return ImageToBase64(image);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Error downloading or converting image from URL to base64: {mess}", ex.Message);
                return null;
            }
        }
    }
}
