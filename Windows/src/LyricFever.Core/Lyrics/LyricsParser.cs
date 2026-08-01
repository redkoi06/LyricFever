namespace LyricFever.Core.Lyrics;

/// <summary>
/// LRC 歌词解析器。行为与 macOS 版 LyricsParser 逐行对齐：
/// - "\\n" 转义换行、去掉首尾引号、按行拆分
/// - [offset:xxx] 头解析
/// - 多时间戳行展开为多行
/// - 结果按时间排序
/// </summary>
public sealed class LyricsParser
{
    public LyricsHeader Header { get; private set; } = new();
    public List<LyricLine> Lyrics { get; private set; } = new();

    public LyricsParser(string lyrics)
    {
        Parse(lyrics);
    }

    private void Parse(string lyrics)
    {
        var lines = lyrics
            .Replace("\\n", "\n")
            .Trim('"', '\'')
            .Trim('\r', '\n')
            .Split('\n');

        foreach (var rawLine in lines)
        {
            ParseLine(rawLine.Trim('\r'));
        }

        Lyrics.Sort((a, b) => a.StartTimeInMs.CompareTo(b.StartTimeInMs));
    }

    private void ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (TryParseHeader("offset", line, out var offsetValue))
        {
            Header.Offset = double.TryParse(offsetValue, out var offset) ? offset : 0;
            return;
        }

        // 不以 ] 结尾的行不是标准歌词行（对应 macOS 的 !hasSuffix("]") 分支）
        if (!line.EndsWith(']'))
        {
            Lyrics.AddRange(ParseLyric(line, Header.Offset));
        }
    }

    private static bool TryParseHeader(string prefix, string line, out string value)
    {
        value = "";
        if (line.StartsWith($"[{prefix}:") && line.EndsWith(']'))
        {
            value = line[(prefix.Length + 2)..^1];
            return true;
        }
        return false;
    }

    private static List<LyricLine> ParseLyric(string line, double headerOffset)
    {
        var cLine = line;
        var timestamps = new List<double>();

        while (cLine.StartsWith('['))
        {
            var closureIndex = cLine.IndexOf(']');
            if (closureIndex < 0) break;

            var amid = cLine[1..closureIndex];
            var timestamp = TimestampMilliseconds(amid);
            if (timestamp == null) return new List<LyricLine>();

            timestamps.Add(timestamp.Value);
            cLine = cLine[(closureIndex + 1)..];
        }

        var words = cLine.Trim();
        return timestamps.Select(ts => new LyricLine(Math.Max(0, ts + headerOffset), words)).ToList();
    }

    /// <summary>
    /// 时间戳解析（对应 macOS timestampMilliseconds）。支持 mm:ss / hh:mm:ss / mm:ss.xx。
    /// 使用 InvariantCulture，避免用户区域设置影响小数解析。
    /// </summary>
    internal static double? TimestampMilliseconds(string value)
    {
        var components = value.Split(':');
        if (components.Length is < 2 or > 3) return null;

        var style = System.Globalization.NumberStyles.Float;
        if (!double.TryParse(components[^1], style, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ||
            !double.TryParse(components[^2], style, System.Globalization.CultureInfo.InvariantCulture, out var minutes) ||
            double.IsNaN(seconds) || double.IsInfinity(seconds) ||
            double.IsNaN(minutes) || double.IsInfinity(minutes) ||
            seconds < 0 || minutes < 0)
            return null;

        double hours = 0;
        if (components.Length == 3)
        {
            if (!double.TryParse(components[0], style, System.Globalization.CultureInfo.InvariantCulture, out hours) ||
                double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0)
                return null;
        }

        return (hours * 3600 + minutes * 60 + seconds) * 1000;
    }
}
