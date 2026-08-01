namespace LyricFever.Core.Lyrics;

/// <summary>
/// 歌词同步引擎（对应 macOS ViewModel 中歌词 updater 的纯逻辑部分）：
/// 播放位置 → 当前行索引。安全边界：空歌词/位置早于首行/非法位置一律返回 null。
/// 循环回绕（位置从结尾跳回开头）由上层 watchdog 检测后调用 Reset，本引擎不做隐式判断。
/// </summary>
public sealed class LyricSyncEngine
{
    private List<LyricLine>? _lyrics;

    public List<LyricLine>? Lyrics
    {
        get => _lyrics;
        set
        {
            _lyrics = value;
            Reset();
        }
    }

    /// <summary>当前高亮行索引（null = 未开始或歌词为空）。</summary>
    public int? CurrentIndex { get; private set; }

    public void Reset() => CurrentIndex = null;

    /// <summary>
    /// 按播放位置更新索引。返回是否发生了可见变化（索引切换或开始/结束）。
    /// 对应 macOS updater：取最后一行 startTime &lt;= position 的索引。
    /// </summary>
    public bool UpdatePosition(double positionMs)
    {
        int? newIndex = IndexForPosition(positionMs);
        if (newIndex == CurrentIndex) return false;
        CurrentIndex = newIndex;
        return true;
    }

    public int? IndexForPosition(double positionMs)
    {
        if (_lyrics == null || _lyrics.Count == 0) return null;
        if (double.IsNaN(positionMs) || double.IsInfinity(positionMs)) return null;
        if (positionMs < _lyrics[0].StartTimeInMs) return null;

        var index = 0;
        for (var i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].StartTimeInMs <= positionMs) index = i;
            else break;
        }
        return index;
    }
}
