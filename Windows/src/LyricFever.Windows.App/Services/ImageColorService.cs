using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricFever.Core.Appearance;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// Decodes album artwork and delegates palette selection to the platform-independent selector.
/// Pixels are explicitly converted to BGRA32 so channel order and stride are deterministic.
/// </summary>
public static class ImageColorService
{
    public static Color? ExtractDominantColor(byte[] imageData)
    {
        if (imageData.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(imageData);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            const double maximumSide = 72;
            var scale = Math.Min(
                Math.Min(maximumSide / frame.PixelWidth, maximumSide / frame.PixelHeight),
                1.0);
            var targetWidth = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));
            var resized = new TransformedBitmap(frame, new ScaleTransform(
                (double)targetWidth / frame.PixelWidth,
                (double)targetHeight / frame.PixelHeight));

            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = resized;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();

            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            var samples = new List<ArtworkColorSample>(width * height);
            const double farthestCorner = 0.7071067811865476;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = y * stride + x * 4;
                    var alpha = pixels[offset + 3];
                    if (alpha < 128) continue;

                    // A mild centre bias reflects the visual focus of most covers without
                    // allowing a small central logo to override the dominant surrounding color.
                    var normalizedX = (x + 0.5) / width - 0.5;
                    var normalizedY = (y + 0.5) / height - 0.5;
                    var distance = Math.Min(1,
                        Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY) / farthestCorner);
                    var weight = (1 + 0.30 * (1 - distance)) * (alpha / 255.0);

                    samples.Add(new ArtworkColorSample(
                        Red: pixels[offset + 2],
                        Green: pixels[offset + 1],
                        Blue: pixels[offset],
                        Weight: weight));
                }
            }

            var selected = AlbumColorPalette.SelectDominantColor(samples);
            return Color.FromRgb(selected.Red, selected.Green, selected.Blue);
        }
        catch (Exception ex)
        {
            AppLog.Error("ArtworkColor", ex);
            return null;
        }
    }
}
