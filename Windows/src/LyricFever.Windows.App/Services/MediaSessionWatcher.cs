using System.Windows.Threading;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;

namespace LyricFever.Windows.App.Services;

/// <summary>从 SMTC 会话读取的曲目信息（对应 macOS ScriptingBridge 的 currentTrack）。</summary>
public sealed class MediaTrackInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public byte[]? ArtworkData { get; set; }
    public string? AppId { get; set; }
    /// <summary>播放进度（毫秒）。</summary>
    public double PositionMs { get; set; }
    public bool IsPlaying { get; set; }
}

/// <summary>
/// SMTC（System Media Transport Controls）监听服务 —— Windows 版 MediaRemote + ScriptingBridge 替代。
/// 读取 SMTC 集成应用（Spotify、Apple Music 等）的播放状态、曲目与进度，并支持播放控制。
/// SpotifyOnly=true（默认）时只接受 Spotify session，其他播放器不当作当前曲目（P0-D）。
/// </summary>
public sealed class MediaSessionWatcher : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly DispatcherTimer _timelineTimer;
    private MediaTrackInfo? _lastTrack;

    /// <summary>曲目切换（Title/Artist 变化）。</summary>
    public event Action<MediaTrackInfo>? TrackChanged;
    /// <summary>播放进度变化（毫秒，500ms 节流）。</summary>
    public event Action<double>? PositionChanged;
    /// <summary>播放/暂停状态变化。</summary>
    public event Action<bool>? PlaybackStateChanged;
    /// <summary>可接受 session 存在性变化（false = “未检测到 Spotify”）。</summary>
    public event Action<bool>? SpotifySessionChanged;

    /// <summary>仅接受 Spotify session（由 UseSpotify 设置驱动）。</summary>
    public bool SpotifyOnly { get; set; } = true;

    public bool HasSpotifySession { get; private set; }

    public bool IsRunning { get; private set; }

    public MediaSessionWatcher()
    {
        _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timelineTimer.Tick += (_, _) => PullTimeline();
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        Attach(_manager.GetCurrentSession());
        _timelineTimer.Start();
        IsRunning = true;
    }

    /// <summary>设置变更后重新评估当前 session（UseSpotify 切换时调用）。</summary>
    public void ApplySessionFilter() => Attach(_manager?.GetCurrentSession());

    private static bool IsSpotifySession(GlobalSystemMediaTransportControlsSession session)
    {
        var appId = session.SourceAppUserModelId;
        if (string.IsNullOrEmpty(appId)) return false;
        // Spotify 桌面客户端与 Microsoft Store 版的 AppId
        return appId.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase)
               || appId.Contains("SpotifyAB.SpotifyMusic", StringComparison.OrdinalIgnoreCase);
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        // P0-D：session 过滤 —— 非 Spotify 播放器不当作当前曲目
        var acceptable = session != null && (!SpotifyOnly || IsSpotifySession(session));
        SetHasSpotifySession(acceptable);
        if (!acceptable) session = null;

        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        _session = session;
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
        _lastTrack = null;
        _ = RefreshTrackAsync();
    }

    private void SetHasSpotifySession(bool has)
    {
        if (HasSpotifySession == has) return;
        HasSpotifySession = has;
        SpotifySessionChanged?.Invoke(has);
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        Attach(_manager?.GetCurrentSession());
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await RefreshTrackAsync();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => RefreshPlaybackState();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => PullTimeline();

    private async Task RefreshTrackAsync()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            // P0-D：异步返回后确认仍是当前 session（防跨 session 覆盖）
            if (props == null || !ReferenceEquals(_session, session)) return;

            var timeline = _session.GetTimelineProperties();
            var playback = _session.GetPlaybackInfo();

            var track = new MediaTrackInfo
            {
                Title = props.Title ?? "",
                Artist = props.Artist ?? "",
                Album = props.AlbumArtist ?? props.AlbumTitle ?? "",
                Duration = timeline.EndTime,
                AppId = _session.SourceAppUserModelId,
                PositionMs = timeline.Position.TotalMilliseconds,
                IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            };

            // 缩略图（异步流 → bytes，供 K 歌背景色提取）
            if (props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    var buffer = new byte[stream.Size];
                    await stream.ReadAsync(buffer.AsBuffer(), (uint)stream.Size, global::Windows.Storage.Streams.InputStreamOptions.None);
                    track.ArtworkData = buffer;
                }
                catch
                {
                    // 缩略图读取失败不影响曲目信息
                }
            }

            var changed = _lastTrack == null
                          || _lastTrack.Title != track.Title
                          || _lastTrack.Artist != track.Artist;
            _lastTrack = track;
            if (changed) TrackChanged?.Invoke(track);
            PlaybackStateChanged?.Invoke(track.IsPlaying);
            PositionChanged?.Invoke(track.PositionMs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][SMTC] refresh track failed: {ex.Message}");
        }
    }

    private void RefreshPlaybackState()
    {
        if (_session == null) return;
        try
        {
            var playback = _session.GetPlaybackInfo();
            var playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PlaybackStateChanged?.Invoke(playing);
        }
        catch { /* ignore */ }
    }

    private void PullTimeline()
    {
        if (_session == null) return;
        try
        {
            var timeline = _session.GetTimelineProperties();
            PositionChanged?.Invoke(timeline.Position.TotalMilliseconds);
        }
        catch { /* ignore */ }
    }

    // ---- 播放控制（对应 ScriptingBridge playpause/nextTrack/previousTrack） ----

    public async Task PlayPauseAsync()
    {
        if (_session == null) return;
        try
        {
            var playback = _session.GetPlaybackInfo();
            if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await _session.TryPauseAsync();
            else
                await _session.TryPlayAsync();
        }
        catch { /* ignore */ }
    }

    public async Task NextAsync()
    {
        if (_session == null) return;
        try { await _session.TrySkipNextAsync(); } catch { /* ignore */ }
    }

    public async Task PreviousAsync()
    {
        if (_session == null) return;
        try { await _session.TrySkipPreviousAsync(); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        _timelineTimer.Stop();
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            _session = null;
        }
    }
}
