using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using LyricFever.Core.Interfaces;
using LyricFever.Core.Lyrics;
using LyricFever.Core.Providers;
using LyricFever.Core.Providers.Spotify;
using LyricFever.Core.Storage;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.Services.Translation;

namespace LyricFever.Windows.App.ViewModels;

/// <summary>
/// 主流程 ViewModel（对应 macOS ViewModel.swift 的歌词状态机）：
/// SMTC 曲目事件 → track ID 映射 → 歌词获取（缓存→Provider 链）→ 同步引擎 → UI 事件。
///
/// 执行指挥书 P0-B 约定：
/// - 每个曲目一个 CancellationTokenSource：切歌/刷新/退出统一取消旧任务
/// - 歌词数组 + 索引 + 派生数组（译文/罗马音）作为一组状态，切歌整组重置
/// - 异步返回时同时校验任务版本与取消状态，旧歌曲结果不得覆盖新歌曲
/// - 循环回绕（进度跳回开头）重置索引
/// - 所有 UI 下标访问防御边界
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MediaSessionWatcher _watcher;
    private readonly SpotifyTrackMapper _trackMapper;
    private readonly LyricFetchService _fetchService;
    private readonly LyricsRepository _repo;
    private readonly TranslationPipelineService _translationPipeline;
    private readonly LyricSyncEngine _sync = new();

    private string? _currentTrackId;
    private string _requestedTrackIdentity = "";
    private DateTimeOffset _emptyRetryNotBefore = DateTimeOffset.MinValue;
    private int _emptyRecoveryAttempt;
    private int _taskVersion;
    private double _lastPositionMs = -1;
    private List<LyricLine>? _currentLyrics;
    private CancellationTokenSource? _currentCts;

    public MainViewModel(MediaSessionWatcher watcher, SpotifyTrackMapper trackMapper,
        LyricFetchService fetchService, LyricsRepository repo,
        TranslationPipelineService translationPipeline)
    {
        _watcher = watcher;
        _trackMapper = trackMapper;
        _fetchService = fetchService;
        _repo = repo;
        _translationPipeline = translationPipeline;

        _watcher.TrackChanged += OnTrackChanged;
        _watcher.PositionChanged += OnPositionChanged;
        _watcher.PlaybackStateChanged += OnPlaybackStateChanged;
        _watcher.MediaSessionAvailabilityChanged += OnMediaSessionAvailabilityChanged;
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
    public bool HasMediaSession { get; private set; }
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
        SetSpotifyLoggedIn(!string.IsNullOrEmpty(cookie));
        await _watcher.StartAsync();
    }

    /// <summary>登录状态回调（登录窗口成功后注入，无需重启）。</summary>
    public void OnSpotifyLoggedIn(string? spDcCookie)
    {
        SetSpotifyLoggedIn(!string.IsNullOrEmpty(spDcCookie));
        // 注入运行中的 Provider（立即生效）
        var app = System.Windows.Application.Current as App;
        if (app != null) app.SpotifyProvider.SpDcCookie = spDcCookie;
        // 清除可能失效的旧 token
        _ = Task.Run(() => app?.SpotifyProvider.ClearAccessToken());
    }

    private void SetSpotifyLoggedIn(bool loggedIn)
    {
        if (IsSpotifyLoggedIn == loggedIn) return;
        IsSpotifyLoggedIn = loggedIn;
        Raise(nameof(IsSpotifyLoggedIn));
    }

    // ---- 曲目事件 ----

    private void OnTrackChanged(MediaTrackInfo track)
    {
        if (string.IsNullOrEmpty(track.Title)) return;
        var identity = TrackIdentity(track);
        var sameRequest = string.Equals(_requestedTrackIdentity, identity, StringComparison.Ordinal);
        // watchdog 和 SMTC 事件可能同时到达：同一曲目的在途任务不得互相取消重启。
        if (sameRequest)
        {
            if (IsFetching || _currentLyrics is { Count: > 0 }) return;
            if (DateTimeOffset.UtcNow < _emptyRetryNotBefore) return;
            AppLog.Info("VM", $"empty lyric watchdog recovery attempt={_emptyRecoveryAttempt + 1}; " +
                              $"title={track.Title}; artist={track.Artist}");
        }
        else
        {
            _emptyRecoveryAttempt = 0;
            _emptyRetryNotBefore = DateTimeOffset.MinValue;
            AppLog.Info("VM", $"track event title={track.Title}; artist={track.Artist}; source={track.AppId}");
        }

        _requestedTrackIdentity = identity;
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

    private void OnPlaybackStateChanged(bool isPlaying)
    {
        if (IsPlaying == isPlaying) return;
        IsPlaying = isPlaying;
        Raise(nameof(IsPlaying));
    }

    /// <summary>首选播放器 session 消失时清空曲目状态。</summary>
    private void OnMediaSessionAvailabilityChanged(bool hasSession)
    {
        HasMediaSession = hasSession;
        Raise(nameof(HasMediaSession));
        if (hasSession) return;
        CancelCurrentWork();
        ResetLyricsState();
        _lastPositionMs = -1;
        CurrentTitle = "";
        CurrentArtist = "";
        CurrentAlbum = "";
        _requestedTrackIdentity = "";
        _emptyRecoveryAttempt = 0;
        _emptyRetryNotBefore = DateTimeOffset.MinValue;
        IsPlaying = false;
        Raise(nameof(IsPlaying));
        RaiseStateChanged();
    }

    // ---- 切歌/刷新统一流程 ----

    private async Task HandleTrackChangeAsync(MediaTrackInfo track)
    {
        // 取消上一曲目的所有在途任务
        CancelCurrentWork();
        var cts = new CancellationTokenSource();
        _currentCts = cts;
        var token = cts.Token;
        var version = ++_taskVersion;

        // 整组状态重置（歌词/索引/译文/罗马音）
        ResetLyricsState();
        CurrentTitle = track.Title;
        CurrentArtist = track.Artist;
        CurrentAlbum = track.Album;
        IsFetching = true;
        RaiseStateChanged();

        try
        {
            // 0. 专辑封面 → K 歌背景色
            if (track.ArtworkData != null)
            {
                var color = await Task.Run(() => ImageColorService.ExtractDominantColor(track.ArtworkData), token);
                if (color != null && version == _taskVersion && !token.IsCancellationRequested)
                {
                    BackgroundColor = color.Value;
                    BackgroundColorChanged?.Invoke(color.Value);
                }
            }

            // 1. SMTC 元数据 → Spotify track ID（带 DB 缓存）
            var resolved = await _trackMapper.ResolveAsync(track.Title, track.Artist,
                string.IsNullOrEmpty(track.Album) ? null : track.Album, token);
            token.ThrowIfCancellationRequested();
            if (version != _taskVersion) return;

            var trackId = resolved?.TrackId ?? AlternativeId(track.Title, track.Artist);

            // 2. 歌词获取（缓存 → Spotify → LRCLIB → NetEase）
            var result = await FetchLyricsWithRetryAsync(trackId, track, version, token);
            token.ThrowIfCancellationRequested();
            if (version != _taskVersion) return;
            AppLog.Info("VM", $"lyrics fetched trackId={trackId}; count={result.Lyrics.Count}");

            // 3. 装载同步引擎（空歌词也装载：显示空状态而非旧歌词）
            _currentTrackId = trackId;
            _currentLyrics = result.Lyrics.Count > 0 ? result.Lyrics : null;
            _sync.Lyrics = _currentLyrics;
            if (_currentLyrics == null)
                ScheduleEmptyRecovery();
            else
            {
                _emptyRecoveryAttempt = 0;
                _emptyRetryNotBefore = DateTimeOffset.MinValue;
            }
            RaiseStateChanged();

            // 4. 翻译/罗马音管线（语言检测 → 产物缓存 → 模型）
            if (_currentLyrics is { Count: > 0 } &&
                (AppSettings.Current.TranslateEnabled || AppSettings.Current.RomanizationEnabled))
            {
                await RequestTranslationAndRomanizationAsync(trackId, _currentLyrics, version, token);
            }
        }
        catch (OperationCanceledException)
        {
            // 切歌/刷新取消，忽略
        }
        catch (Exception ex)
        {
            AppLog.Error("VM", ex);
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

    private async Task<LyricFetchResult> FetchLyricsWithRetryAsync(
        string trackId, MediaTrackInfo track, int version, CancellationToken token)
    {
        var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };
        LyricFetchResult result = new();
        for (var attempt = 0; attempt <= delays.Length; attempt++)
        {
            result = await _fetchService.FetchAsync(trackId, track.Title, track.Artist,
                string.IsNullOrEmpty(track.Album) ? null : track.Album, token);
            token.ThrowIfCancellationRequested();
            if (version != _taskVersion) throw new OperationCanceledException();
            if (result.Lyrics.Count > 0) return result;
            if (attempt == delays.Length) break;

            AppLog.Info("VM", $"lyrics empty; retry={attempt + 1}; title={track.Title}; artist={track.Artist}");
            await Task.Delay(delays[attempt], token);
        }
        return result;
    }

    private void ScheduleEmptyRecovery()
    {
        var delays = new[] { 10, 30, 60 };
        var seconds = delays[Math.Min(_emptyRecoveryAttempt, delays.Length - 1)];
        _emptyRecoveryAttempt++;
        _emptyRetryNotBefore = DateTimeOffset.UtcNow.AddSeconds(seconds);
        AppLog.Info("VM", $"lyrics remain empty; watchdog retry in {seconds}s");
    }

    /// <summary>刷新歌词：与切歌共用同一套"取消→清空→获取→校验→设置→产物"流程。</summary>
    public void RefreshLyrics()
    {
        if (_currentTrackId == null) return;
        var track = new MediaTrackInfo
        {
            Title = CurrentTitle,
            Artist = CurrentArtist,
            Album = CurrentAlbum
        };
        _ = HandleTrackChangeAsync(track);
    }

    private void CancelCurrentWork()
    {
        var cts = _currentCts;
        _currentCts = null;
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void ResetLyricsState()
    {
        _sync.Lyrics = null;
        _currentLyrics = null;
        _currentTrackId = null;
        TranslatedLyrics = new List<string>();
        RomanizedLyrics = new List<string>();
    }

    /// <summary>
    /// 翻译/罗马音管线（产物缓存 → 批量翻译 + 罗马音并行 → 缓存写回）。
    /// 任务版本 + 取消 token 双重校验：处理期间切歌则丢弃结果。
    /// </summary>
    private async Task RequestTranslationAndRomanizationAsync(string trackId, List<LyricLine> lyrics, int version,
        CancellationToken token)
    {
        // 设置页 SourceLanguage 覆盖自动检测（auto → 自动识别）
        var lang = AppSettings.Current.SourceLanguage switch
        {
            "en" => LyricLanguage.English,
            "ja" => LyricLanguage.Japanese,
            _ => LanguageDetector.Detect(lyrics)
        };
        if (lang is not (LyricLanguage.English or LyricLanguage.Japanese)) return;

        var (translated, romanized) = await _translationPipeline.ProcessAsync(
            trackId, lyrics, lang,
            CurrentTitle, CurrentArtist, CurrentAlbum,
            AppSettings.Current.TranslateEnabled,
            AppSettings.Current.RomanizationEnabled,
            isCurrent: () => version == _taskVersion && !token.IsCancellationRequested,
            token);

        if (version != _taskVersion || token.IsCancellationRequested) return;

        TranslatedLyrics = translated;
        RomanizedLyrics = romanized;
        RaiseStateChanged(); // K 歌窗口重建三层歌词
    }

    // ---- 播放控制（托盘菜单用） ----

    public Task PlayPauseAsync() => _watcher.PlayPauseAsync();
    public Task NextAsync() => _watcher.NextAsync();
    public Task PreviousAsync() => _watcher.PreviousAsync();

    /// <summary>替代 ID（对应 macOS alternativeID）：歌手+歌名稳定哈希，供非 Spotify 来源兜底。</summary>
    internal static string AlternativeId(string title, string artist)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{artist}|{title}"));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }

    private static string TrackIdentity(MediaTrackInfo track) =>
        $"{track.Title.Trim()}\u001f{track.Artist.Trim()}\u001f{track.Album.Trim()}";

    public void Dispose()
    {
        _watcher.TrackChanged -= OnTrackChanged;
        _watcher.PositionChanged -= OnPositionChanged;
        _watcher.PlaybackStateChanged -= OnPlaybackStateChanged;
        _watcher.MediaSessionAvailabilityChanged -= OnMediaSessionAvailabilityChanged;
        CancelCurrentWork();
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
