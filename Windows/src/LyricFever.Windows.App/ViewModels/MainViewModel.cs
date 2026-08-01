using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using LyricFever.Core.Interfaces;
using LyricFever.Core.Lyrics;
using LyricFever.Core.Providers;
using LyricFever.Core.Providers.Spotify;
using LyricFever.Core.Storage;
using LyricFever.Windows.App.Services;

namespace LyricFever.Windows.App.ViewModels;

/// <summary>
/// 主流程 ViewModel（对应 macOS ViewModel.swift 的歌词状态机）：
/// SMTC 曲目事件 → track ID 映射 → 歌词获取（缓存→Provider 链）→ 同步引擎 → UI 事件。
///
/// 移植自 AGENTS.md 沉淀的防崩溃/防竞态经验：
/// - 歌词数组 + 索引 + 派生数组（译文/罗马音）作为一组状态，切歌整组重置
/// - 异步结果返回时校验任务版本，旧歌曲结果不得覆盖新歌曲
/// - 循环回绕（进度跳回开头）重置索引
/// - 所有 UI 下标访问防御边界
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly MediaSessionWatcher _watcher;
    private readonly SpotifyTrackMapper _trackMapper;
    private readonly LyricFetchService _fetchService;
    private readonly LyricsRepository _repo;
    private readonly LyricSyncEngine _sync = new();

    private string? _currentTrackId;
    private int _taskVersion;
    private double _lastPositionMs = -1;

    public MainViewModel(MediaSessionWatcher watcher, SpotifyTrackMapper trackMapper,
        LyricFetchService fetchService, LyricsRepository repo)
    {
        _watcher = watcher;
        _trackMapper = trackMapper;
        _fetchService = fetchService;
        _repo = repo;

        _watcher.TrackChanged += OnTrackChanged;
        _watcher.PositionChanged += OnPositionChanged;
    }

    // ---- 对外状态 ----

    public List<LyricLine>? CurrentlyPlayingLyrics => _sync.Lyrics;

    /// <summary>当前高亮行索引（null = 未开始）。</summary>
    public int? CurrentlyPlayingLyricsIndex => _sync.CurrentIndex;

    public List<string> TranslatedLyrics { get; private set; } = new();
    public List<string> RomanizedLyrics { get; private set; } = new();

    public string? CurrentTrackId => _currentTrackId;
    public string CurrentTitle { get; private set; } = "";
    public string CurrentArtist { get; private set; } = "";
    public string CurrentAlbum { get; private set; } = "";
    public bool IsFetching { get; private set; }
    public bool IsSpotifyLoggedIn { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>索引变化（K 歌窗口驱动高亮）。</summary>
    public event Action? IndexChanged;
    /// <summary>整组歌词状态变化（切歌/刷新后 UI 重建）。</summary>
    public event Action? LyricsStateChanged;
    /// <summary>背景色变化（K 歌窗口背景）。</summary>
    public event Action<System.Windows.Media.Color>? BackgroundColorChanged;
    /// <summary>状态变化（PropertyChanged 绑定）。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前 K 歌背景色（专辑封面提取）。</summary>
    public System.Windows.Media.Color BackgroundColor { get; private set; } =
        System.Windows.Media.Color.FromRgb(30, 30, 34);

    // ---- 初始化 ----

    public async Task StartAsync()
    {
        // 恢复 sp_dc → 判定登录状态
        var cookie = CredentialStore.Get("spotify.sp_dc");
        if (!string.IsNullOrEmpty(cookie))
        {
            IsSpotifyLoggedIn = true;
            Raise(nameof(IsSpotifyLoggedIn));
        }
        await _watcher.StartAsync();
    }

    // ---- 曲目事件 ----

    private void OnTrackChanged(MediaTrackInfo track)
    {
        if (string.IsNullOrEmpty(track.Title)) return;
        // 同曲目跳过（SMTC 重复通知）
        if (_currentTrackId != null && _lastPositionMs >= 0 &&
            CurrentTitle == track.Title && CurrentArtist == track.Artist)
            return;

        _ = HandleTrackChangeAsync(track);
    }

    private void OnPositionChanged(double positionMs)
    {
        // 循环回绕检测：进度从大值跳回开头 → 重置索引（对应 macOS watchdog）
        if (_lastPositionMs > 30000 && positionMs < 5000)
        {
            _sync.Reset();
            RaiseIndex();
        }
        _lastPositionMs = positionMs;

        // 偏移补偿（正数提前显示）
        var offset = AppSettings.Current.LyricOffsetMs;
        if (_sync.UpdatePosition(positionMs + offset))
        {
            RaiseIndex();
        }
    }

    // ---- 切歌流程 ----

    private async Task HandleTrackChangeAsync(MediaTrackInfo track)
    {
        var version = ++_taskVersion;

        // 整组状态重置（歌词/索引/译文/罗马音）
        _sync.Lyrics = null;
        TranslatedLyrics = new List<string>();
        RomanizedLyrics = new List<string>();
        _currentTrackId = null;
        CurrentTitle = track.Title;
        CurrentArtist = track.Artist;
        CurrentAlbum = track.Album;
        IsFetching = true;
        RaiseStateChanged();

        try
        {
            // 0. 专辑封面 → K 歌背景色（对应 macOS ColorKit 提取，含 IDToColor 缓存）
            if (track.ArtworkData != null)
            {
                var color = ImageColorService.ExtractDominantColor(track.ArtworkData);
                if (color != null)
                {
                    BackgroundColor = color.Value;
                    BackgroundColorChanged?.Invoke(color.Value);
                }
            }

            // 1. SMTC 元数据 → Spotify track ID（带 DB 缓存）
            var resolved = await _trackMapper.ResolveAsync(track.Title, track.Artist,
                string.IsNullOrEmpty(track.Album) ? null : track.Album);
            if (version != _taskVersion) return;

            var trackId = resolved?.TrackId ?? AlternativeId(track.Title, track.Artist);

            // 2. 歌词获取（缓存 → Spotify → LRCLIB → NetEase）
            var result = await _fetchService.FetchAsync(trackId, track.Title, track.Artist,
                string.IsNullOrEmpty(track.Album) ? null : track.Album);
            if (version != _taskVersion) return;

            // 3. 装载同步引擎（空歌词也装载：显示空状态而非旧歌词）
            _currentTrackId = trackId;
            _sync.Lyrics = result.Lyrics.Count > 0 ? result.Lyrics : null;
            RaiseStateChanged();

            // 4. 翻译/罗马音管线（P4 接入）：语言检测 → 产物缓存 → 模型
            if (AppSettings.Current.TranslateEnabled || AppSettings.Current.RomanizationEnabled)
            {
                await RequestTranslationAndRomanizationAsync(trackId, result.Lyrics, version);
            }
        }
        catch (OperationCanceledException)
        {
            // 切歌取消，忽略
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][VM] track change failed: {ex.Message}");
        }
        finally
        {
            if (version == _taskVersion)
            {
                IsFetching = false;
                RaiseStateChanged();
            }
        }
    }

    /// <summary>翻译/罗马音请求（P4 实现管线，当前保留钩子）。</summary>
    private async Task RequestTranslationAndRomanizationAsync(string trackId, List<LyricLine> lyrics, int version)
    {
        var lang = LanguageDetector.Detect(lyrics);

        // 日语 → 罗马音
        if (AppSettings.Current.RomanizationEnabled && lang == LyricLanguage.Japanese)
        {
            // P4：IRomanizationProvider 接入 + 缓存
        }

        // 翻译（en/ja → zh）
        if (AppSettings.Current.TranslateEnabled && lang is LyricLanguage.English or LyricLanguage.Japanese)
        {
            // P4：ITranslationProvider 接入 + 产物缓存（TranslationCache）
        }
        await Task.CompletedTask;
    }

    // ---- 播放控制（托盘菜单用） ----

    public Task PlayPauseAsync() => _watcher.PlayPauseAsync();
    public Task NextAsync() => _watcher.NextAsync();
    public Task PreviousAsync() => _watcher.PreviousAsync();

    public void RefreshLyrics()
    {
        if (_currentTrackId == null) return;
        var version = ++_taskVersion;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _fetchService.FetchAsync(_currentTrackId, CurrentTitle, CurrentArtist,
                    string.IsNullOrEmpty(CurrentAlbum) ? null : CurrentAlbum);
                if (version != _taskVersion) return;
                _sync.Lyrics = result.Lyrics.Count > 0 ? result.Lyrics : null;
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricFever][VM] refresh failed: {ex.Message}");
            }
        });
    }

    /// <summary>替代 ID（对应 macOS alternativeID）：歌手+歌名稳定哈希，供非 Spotify 来源兜底。</summary>
    internal static string AlternativeId(string title, string artist)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{artist}|{title}"));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }

    // ---- 通知 ----

    private void RaiseIndex() => IndexChanged?.Invoke();

    private void RaiseStateChanged()
    {
        Raise(nameof(IsFetching));
        Raise(nameof(CurrentTitle));
        Raise(nameof(CurrentlyPlayingLyrics));
        LyricsStateChanged?.Invoke();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
