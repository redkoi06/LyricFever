namespace LyricFever.Core.Lyrics;

/// <summary>
/// Repairs a common provider segmentation mismatch: one complete human translation is attached
/// to the first of two consecutive source lines while the second translation slot is empty.
/// </summary>
public static class HumanTranslationContinuity
{
    private const double MaximumCarryForwardGapMs = 15_000;
    private const string InstrumentalMarkers = "♪♫♬♩";

    /// <summary>
    /// Reuses an original non-empty translation for the immediately following displayable source
    /// line when that line has no translation. Existing translations are never overwritten, the
    /// repair never cascades through multiple empty slots, and long/instrumental gaps are skipped.
    /// </summary>
    public static List<string> ReusePreviousForMissingNextLine(
        IReadOnlyList<LyricLine> sourceLyrics,
        IReadOnlyList<string> translatedLyrics)
    {
        var result = new List<string>(sourceLyrics.Count);
        for (var index = 0; index < sourceLyrics.Count; index++)
        {
            var translated = index < translatedLyrics.Count
                ? translatedLyrics[index]?.Trim() ?? ""
                : "";
            result.Add(translated);
        }

        for (var index = 1; index < sourceLyrics.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(result[index])) continue;
            if (!IsDisplayable(sourceLyrics[index].Words) ||
                !IsDisplayable(sourceLyrics[index - 1].Words))
                continue;

            // Read from the original input rather than result so one translation is reused only
            // for its immediate next line and cannot leak through a long run of missing entries.
            var previousTranslation = index - 1 < translatedLyrics.Count
                ? translatedLyrics[index - 1]?.Trim() ?? ""
                : "";
            if (previousTranslation.Length == 0) continue;

            var previousTime = sourceLyrics[index - 1].StartTimeInMs;
            var currentTime = sourceLyrics[index].StartTimeInMs;
            if (!double.IsFinite(previousTime) || !double.IsFinite(currentTime)) continue;
            var gap = currentTime - previousTime;
            if (gap < 0 || gap > MaximumCarryForwardGapMs) continue;

            result[index] = previousTranslation;
        }

        return result;
    }

    private static bool IsDisplayable(string? text)
    {
        var trimmed = text?.Trim();
        return !string.IsNullOrEmpty(trimmed) &&
               trimmed.Any(character => !char.IsWhiteSpace(character) &&
                                        !InstrumentalMarkers.Contains(character));
    }
}
