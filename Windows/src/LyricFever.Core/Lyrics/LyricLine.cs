using System.Text.Json.Serialization;

namespace LyricFever.Core.Lyrics;

/// <summary>
/// 歌词行（对应 macOS LyricLine）。startTimeMs 使用字符串保存，以兼容网络歌词中的文本时间戳。
/// </summary>
public sealed class LyricLine
{
    [JsonPropertyName("startTimeMs")]
    public string StartTimeMs { get; set; } = "";

    [JsonPropertyName("words")]
    public string Words { get; set; } = "";

    [JsonIgnore]
    public string LineId { get; } = Guid.NewGuid().ToString("N");

    [JsonIgnore]
    public double StartTimeInMs
    {
        get
        {
            // 与 macOS 版一致：无效时间戳视为解码错误，由调用方兜底
            if (double.TryParse(StartTimeMs, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ms) &&
                !double.IsNaN(ms) && !double.IsInfinity(ms))
                return ms;
            return double.NaN;
        }
    }

    public LyricLine() { }

    public LyricLine(double startTimeMs, string words)
    {
        StartTimeMs = startTimeMs.ToString("0.###");
        Words = words;
    }

    public override string ToString() => $"[{StartTimeMs}ms] {Words}";
}
