namespace LyricFever.Core.Appearance;

/// <summary>A weighted artwork pixel used by the platform-independent palette selector.</summary>
public readonly record struct ArtworkColorSample(byte Red, byte Green, byte Blue, double Weight = 1);

/// <summary>A platform-independent sRGB color.</summary>
public readonly record struct ArtworkColor(byte Red, byte Green, byte Blue);

/// <summary>
/// Selects a representative album color and turns it into a background that keeps white lyrics
/// legible. Hue-based buckets prevent a single vivid pixel from winning over a real dominant area.
/// </summary>
public static class AlbumColorPalette
{
    public const double MinimumWhiteContrast = 5.5;

    public static ArtworkColor SelectBackground(IEnumerable<ArtworkColorSample> samples)
        => NormalizeForWhiteText(SelectDominantColor(samples));

    public static ArtworkColor SelectDominantColor(IEnumerable<ArtworkColorSample> samples)
    {
        var buckets = new Dictionary<int, ColorBucket>();
        double totalWeight = 0;

        foreach (var sample in samples)
        {
            if (!double.IsFinite(sample.Weight) || sample.Weight <= 0) continue;

            var (hue, saturation, value) = RgbToHsv(sample.Red, sample.Green, sample.Blue);
            var hueBin = saturation < 0.08 ? 24 : Math.Min(23, (int)(hue / 15));
            var saturationBin = Math.Min(3, (int)(saturation * 4));
            var valueBin = Math.Min(3, (int)(value * 4));
            var key = (hueBin << 4) | (saturationBin << 2) | valueBin;

            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new ColorBucket();
                buckets[key] = bucket;
            }

            bucket.Add(sample);
            totalWeight += sample.Weight;
        }

        if (buckets.Count == 0 || totalWeight <= 0)
            return new ArtworkColor(35, 37, 44);

        ColorBucket? best = null;
        double bestScore = double.MinValue;
        foreach (var bucket in buckets.Values)
        {
            var color = bucket.Average;
            var (_, saturation, value) = RgbToHsv(color.Red, color.Green, color.Blue);
            var population = bucket.Weight / totalWeight;

            // Population remains the primary signal. Saturation can promote a substantial accent,
            // but cannot let a handful of vivid pixels displace the artwork's actual palette.
            var colorfulness = 0.35 + saturation * 1.15;
            var tonePenalty = value < 0.08 || (value > 0.94 && saturation < 0.12) ? 0.55 : 1.0;
            var score = Math.Pow(population, 0.70) * colorfulness * tonePenalty;
            if (score <= bestScore) continue;

            best = bucket;
            bestScore = score;
        }

        return best?.Average ?? new ArtworkColor(35, 37, 44);
    }

    public static ArtworkColor NormalizeForWhiteText(ArtworkColor color, double backgroundOpacity = 1)
    {
        backgroundOpacity = Math.Clamp(backgroundOpacity, 0, 1);
        var (hue, saturation, value) = RgbToHsv(color.Red, color.Green, color.Blue);
        if (saturation >= 0.10)
            saturation = Math.Clamp(saturation * 1.14 + 0.04, 0.18, 0.90);

        // Lift almost-black covers enough to remain recognisably colored, while capping bright
        // artwork before the contrast pass. The final binary search preserves hue and saturation.
        var preferredValue = Math.Clamp(value * 0.78 + 0.08,
            saturation >= 0.10 ? 0.34 : 0.22,
            0.56);
        var candidate = HsvToRgb(hue, saturation, preferredValue);
        if (ContrastWithWhite(candidate, backgroundOpacity) >= MinimumWhiteContrast) return candidate;

        var low = 0.08;
        var high = preferredValue;
        for (var iteration = 0; iteration < 18; iteration++)
        {
            var midpoint = (low + high) / 2;
            var probe = HsvToRgb(hue, saturation, midpoint);
            if (ContrastWithWhite(probe, backgroundOpacity) >= MinimumWhiteContrast)
                low = midpoint;
            else
                high = midpoint;
        }

        return HsvToRgb(hue, saturation, low);
    }

    public static double ContrastWithWhite(ArtworkColor color, double backgroundOpacity = 1)
    {
        backgroundOpacity = Math.Clamp(backgroundOpacity, 0, 1);
        var composited = new ArtworkColor(
            (byte)Math.Round(color.Red * backgroundOpacity + 255 * (1 - backgroundOpacity)),
            (byte)Math.Round(color.Green * backgroundOpacity + 255 * (1 - backgroundOpacity)),
            (byte)Math.Round(color.Blue * backgroundOpacity + 255 * (1 - backgroundOpacity)));
        var backgroundLuminance = RelativeLuminance(composited);
        return 1.05 / (backgroundLuminance + 0.05);
    }

    private static double RelativeLuminance(ArtworkColor color)
    {
        static double Linearize(byte component)
        {
            var value = component / 255.0;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return Linearize(color.Red) * 0.2126 +
               Linearize(color.Green) * 0.7152 +
               Linearize(color.Blue) * 0.0722;
    }

    private static (double Hue, double Saturation, double Value) RgbToHsv(byte red, byte green, byte blue)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta == 0)
            hue = 0;
        else if (max == r)
            hue = 60 * (((g - b) / delta) % 6);
        else if (max == g)
            hue = 60 * (((b - r) / delta) + 2);
        else
            hue = 60 * (((r - g) / delta) + 4);

        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    private static ArtworkColor HsvToRgb(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
        var match = value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new ArtworkColor(
            (byte)Math.Round((r + match) * 255),
            (byte)Math.Round((g + match) * 255),
            (byte)Math.Round((b + match) * 255));
    }

    private sealed class ColorBucket
    {
        private double _red;
        private double _green;
        private double _blue;

        public double Weight { get; private set; }

        public ArtworkColor Average => Weight <= 0
            ? new ArtworkColor(35, 37, 44)
            : new ArtworkColor(
                (byte)Math.Round(_red / Weight),
                (byte)Math.Round(_green / Weight),
                (byte)Math.Round(_blue / Weight));

        public void Add(ArtworkColorSample sample)
        {
            _red += sample.Red * sample.Weight;
            _green += sample.Green * sample.Weight;
            _blue += sample.Blue * sample.Weight;
            Weight += sample.Weight;
        }
    }
}
