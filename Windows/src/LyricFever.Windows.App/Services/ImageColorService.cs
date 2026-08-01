using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 专辑封面 → 背景色提取（对应 macOS ImageColorGeneration 的简化实现）：
/// 降采样后扫描像素，优先挑"白字可读的最饱和主色"，否则返回平均色。
/// </summary>
public static class ImageColorService
{
    /// <summary>从图片字节提取背景色。失败时返回 null。</summary>
    public static Color? ExtractDominantColor(byte[] imageData)
    {
        try
        {
            using var ms = new MemoryStream(imageData);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            // 降采样到 64x64 内（对应 ColorKit kMeans 的等效效果，开销极低）
            var scale = Math.Min(Math.Min(64.0 / frame.PixelWidth, 64.0 / frame.PixelHeight), 1.0);
            var width = Math.Max(1, (int)(frame.PixelWidth * scale));
            var height = Math.Max(1, (int)(frame.PixelHeight * scale));

            var resized = new TransformedBitmap(frame, new ScaleTransform(
                (double)width / frame.PixelWidth, (double)height / frame.PixelHeight));
            var pixels = new byte[width * height * 4];
            resized.CopyPixels(pixels, width * 4, 0);

            return ComputeLegibleColor(pixels, width, height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 白字可读颜色：饱和度 ≥ 0.25 且亮度适中（0.2~0.85）的像素中取最饱和的；
    /// 无候选时返回全图平均色（对应 macOS findWhiteTextLegibleMostSaturatedDominantColor 的简化版）。
    /// </summary>
    private static Color ComputeLegibleColor(byte[] pixels, int width, int height)
    {
        Color? best = null;
        double bestSaturation = -1;
        long rSum = 0, gSum = 0, bSum = 0;
        var count = 0;
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var r = pixels[i];
                var g = pixels[i + 1];
                var b = pixels[i + 2];
                // 忽略 alpha 低的像素
                if (i + 3 < pixels.Length && pixels[i + 3] < 128) continue;

                rSum += r; gSum += g; bSum += b;
                count++;

                // 中心区域加权（专辑封面中心通常是主视觉）
                var dist = Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                var radius = Math.Min(width, height) * 0.5;
                if (dist > radius) continue;

                var (hue, sat, lum) = RgbToHsl(r, g, b);
                if (sat >= 0.25 && lum is >= 0.2 and <= 0.85 && sat > bestSaturation)
                {
                    bestSaturation = sat;
                    best = Color.FromRgb(r, g, b);
                }
            }
        }

        if (best.HasValue && count > 0) return best.Value;
        if (count == 0) return Color.FromRgb(30, 30, 34);
        return Color.FromRgb((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count));
    }

    /// <summary>RGB → HSL（饱和度/亮度）。</summary>
    private static (double Hue, double Saturation, double Lightness) RgbToHsl(byte r, byte g, byte b)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var l = (max + min) / 2.0;
        var d = max - min;

        double h;
        if (d == 0)
        {
            h = 0;
        }
        else if (max == rd)
        {
            h = 60 * (((gd - bd) / d) % 6);
        }
        else if (max == gd)
        {
            h = 60 * (((bd - rd) / d) + 2);
        }
        else
        {
            h = 60 * (((rd - gd) / d) + 4);
        }
        if (h < 0) h += 360;

        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (h, s, l);
    }
}
