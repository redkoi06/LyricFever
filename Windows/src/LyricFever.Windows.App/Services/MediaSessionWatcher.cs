using System.Windows.Threading;
using System.Runtime.InteropServices.WindowsRuntime;
using LyricFever.Core.Lyrics;
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
/// 按用户选择显式绑定 Apple Music、Spotify 或系统当前播放器，避免把浏览器误当成当前曲目。
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
    /// <summary>首选播放器 session 存在性变化。</summary>
    public event Action<bool>? MediaSessionAvailabilityChanged;

    public MediaPlayerPreference PreferredPlayer { get; set; } = MediaPlayerPreference.AppleMusic;

    public bool HasMediaSession { get; private set; }
    public string? CurrentSourceAppId => _session?.SourceAppUserModelId;

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
        AppLog.Info("SMTC", $"manager ready; preferred={PreferredPlayer}");
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        Attach(SelectPreferredSession());
        _timelineTimer.Start();
        IsRunning = true;
    }

    /// <summary>设置变更后重新评估当前 session。</summary>
    public void ApplySessionFilter() => Attach(SelectPreferredSession());

    private GlobalSystemMediaTransportControlsSession? SelectPreferredSession()
    {
        if (_manager == null) return null;
        if (PreferredPlayer == MediaPlayerPreference.Any)
            return _manager.GetCurrentSession();

        var sessions = _manager.GetSessions();
        AppLog.Info("SMTC", $"sessions=[{string.Join(", ", sessions.Select(session => session.SourceAppUserModelId))}]");
        return PreferredPlayer == MediaPlayerPreference.AppleMusic
            ? sessions.FirstOrDefault(session => IsAppleMusicAppId(session.SourceAppUserModelId))
            : sessions.FirstOrDefault(session => IsSpotifyAppId(session.SourceAppUserModelId));
    }

    internal static bool IsAppleMusicAppId(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) &&
        (appId.Contains("AppleInc.AppleMusicWin", StringComparison.OrdinalIgnoreCase)
         || appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));

    internal static bool IsSpotifyAppId(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;
        // Spotify 桌面客户端与 Microsoft Store 版的 AppId
        return appId.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase)
               || appId.Contains("SpotifyAB.SpotifyMusic", StringComparison.OrdinalIgnoreCase);
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        AppLog.Info("SMTC", $"attach source={session?.SourceAppUserModelId ?? "<none>"}");
        SetHasMediaSession(session != null);

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

    private void SetHasMediaSession(bool has)
    {
        if (HasMediaSession == has) return;
        HasMediaSession = has;
        MediaSessionAvailabilityChanged?.Invoke(has);
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        Attach(SelectPreferredSession());
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => Attach(SelectPreferredSession());

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
            var sourceAppId = _session.SourceAppUserModelId;
            var rawAlbum = !string.IsNullOrWhiteSpace(props.AlbumTitle) ? props.AlbumTitle : props.AlbumArtist ?? "";
            var normalized = IsAppleMusicAppId(sourceAppId)
                ? AppleMusicMetadataNormalizer.Normalize(props.Artist, rawAlbum)
                : (props.Artist?.Trim() ?? "", rawAlbum.Trim());

            var track = new MediaTrackInfo
            {
                Title = props.Title ?? "",
                Artist = normalized.Item1,
                Album = normalized.Item2,
                Duration = timeline.EndTime,
                AppId = sourceAppId,
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
            AppLog.Info("SMTC", $"track title={track.Title}; artist={track.Artist}; album={track.Album}; playing={track.IsPlaying}; source={track.AppId}");
            if (changed) TrackChanged?.Invoke(track);
            PlaybackStateChanged?.Invoke(track.IsPlaying);
            PositionChanged?.Invoke(track.PositionMs);
        }
        catch (Exception ex)
        {
            AppLog.Error("SMTC", ex);
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
            _manager.SessionsChanged -= OnSessionsChanged;
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

public enum MediaPlayerPreference
{
    AppleMusic,
    Spotify,
    Any
}
