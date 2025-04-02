#pragma warning disable CA1416 // Validate platform compatibility
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace AIToolkit
{
    public static class ImageUtils
    {

        /// <summary>
        /// Scales an image proportionally so that neither dimension exceeds the specified maximum size.
        /// </summary>
        /// <param name="image">The source image to scale</param>
        /// <param name="maxSize">The maximum width or height allowed</param>
        /// <returns>A new scaled image if resizing was needed, or the original image if already within limits</returns>
        public static Image ScaleImage(Image image, int maxSize)
        {
            // Get the original dimensions
            int originalWidth = image.Width;
            int originalHeight = image.Height;

            // If the image is already smaller than the max size in both dimensions, return it unchanged
            if (originalWidth <= maxSize && originalHeight <= maxSize)
                return image;

            // Calculate the scaling factor to maintain aspect ratio
            float scaleFactor;
            if (originalWidth > originalHeight)
                scaleFactor = (float)maxSize / originalWidth;
            else
                scaleFactor = (float)maxSize / originalHeight;

            // Calculate new dimensions
            int newWidth = (int)(originalWidth * scaleFactor);
            int newHeight = (int)(originalHeight * scaleFactor);

            // Ensure we don't have dimensions of 0
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);

            // Create a new bitmap with the calculated dimensions
            Bitmap scaledImage = new(newWidth, newHeight);

            // Draw the original image onto the new bitmap with scaling
            using (Graphics graphics = Graphics.FromImage(scaledImage))
            {
                // Set the interpolation mode for better quality
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // Draw the image
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);
            }

            return scaledImage;
        }

        /// <summary>
        /// Converts an image file to a base64 encoded string suitable for the KoboldCpp API.
        /// This encodes the actual pixel data of the image, with optional scaling.
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="maxSize">Optional maximum size for width or height. If specified, image will be scaled down proportionally if needed.</param>
        /// <returns>Base64 encoded string of the image data</returns>
        public static string? ImageToBase64(string imagePath, int maxSize = 0)
        {
            try
            {
                using var image = Image.FromFile(imagePath);
                return ImageToBase64(image, maxSize);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting image to base64: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts an in-memory image to a base64 encoded string, with optional scaling.
        /// </summary>
        /// <param name="image">The image to convert</param>
        /// <param name="maxSize">Optional maximum size for width or height. If specified, image will be scaled down proportionally if needed.</param>
        /// <returns>Base64 encoded string of the image data</returns>
        public static string? ImageToBase64(Image image, int maxSize = 0)
        {
            try
            {
                // Scale the image if maxSize is specified and greater than 0
                Image imageToUse = image;
                bool needToDispose = false;

                if (maxSize > 0)
                {
                    imageToUse = ScaleImage(image, maxSize);
                    // Only flag for disposal if a new image was created
                    needToDispose = !ReferenceEquals(imageToUse, image);
                }

                try
                {
                    using MemoryStream ms = new();
                    // Save the image to the memory stream in PNG format
                    imageToUse.Save(ms, ImageFormat.Png);
                    ms.Position = 0;

                    byte[] imageBytes = ms.ToArray();
                    return Convert.ToBase64String(imageBytes);
                }
                finally
                {
                    // Dispose the scaled image if we created a new one
                    if (needToDispose && imageToUse != null)
                    {
                        imageToUse.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting image to base64: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads an image from a URL and converts it to a base64 encoded string
        /// </summary>
        /// <param name="imageUrl">URL of the image</param>
        /// <returns>Base64 encoded string of the image data</returns>
        public static async Task<string?> ImageFromUrlToBase64Async(string imageUrl)
        {
            try
            {
                using HttpClient httpClient = new();
                byte[] imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
                using MemoryStream ms = new(imageBytes);
                using Image image = Image.FromStream(ms);
                return ImageToBase64(image);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading or converting image from URL to base64: {ex.Message}");
                return null;
            }
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
