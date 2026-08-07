using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Threading;
using LyricFever.Core.Lyrics;
using Windows.Media.Control;

namespace LyricFever.Windows.App.Services;

/// <summary>从 SMTC 会话读取的当前曲目信息。</summary>
public sealed class MediaTrackInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public byte[]? ArtworkData { get; set; }
    public string? AppId { get; set; }
    public double PositionMs { get; set; }
    public bool IsPlaying { get; set; }
}

/// <summary>
/// Windows SMTC 播放监听。事件用于低延迟更新，2 秒 watchdog 用于恢复 Apple Music
/// 偶发漏发元数据事件、短暂丢失 session 或先返回空标题的情况。
/// </summary>
public sealed class MediaSessionWatcher : IDisposable
{
    private static readonly TimeSpan SessionLossGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan[] EmptyMetadataRetryDelays =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(1500)];

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly DispatcherTimer _timelineTimer;
    private readonly DispatcherTimer _watchdogTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly PlaybackPositionStabilizer _positionStabilizer = new();
    private MediaTrackInfo? _lastTrack;
    private DateTimeOffset? _sessionMissingSince;
    private string _lastSessionInventory = "";
    private int _sessionRevision;
    private int _refreshPending;
    private bool _disposed;

    public event Action<MediaTrackInfo>? TrackChanged;
    public event Action<double>? PositionChanged;
    public event Action<bool>? PlaybackStateChanged;
    public event Action<bool>? MediaSessionAvailabilityChanged;

    public MediaPlayerPreference PreferredPlayer { get; set; } = MediaPlayerPreference.AppleMusic;

    public bool HasMediaSession { get; private set; }
    public string? CurrentSourceAppId => _session?.SourceAppUserModelId;
    public bool IsRunning { get; private set; }

    public MediaSessionWatcher()
    {
        _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timelineTimer.Tick += (_, _) => PullTimeline();
        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchdogTimer.Tick += (_, _) => WatchdogTick();
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        AppLog.Info("SMTC", $"manager ready; preferred={PreferredPlayer}");
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;

        var selected = SelectPreferredSession(logInventory: true);
        Attach(selected);
        _timelineTimer.Start();
        _watchdogTimer.Start();
        IsRunning = true;
    }

    /// <summary>设置变更后立即重新评估当前 session，不沿用旧播放器。</summary>
    public void ApplySessionFilter() => ReevaluateSession(allowGrace: false, logInventory: true);

    private GlobalSystemMediaTransportControlsSession? SelectPreferredSession(bool logInventory)
    {
        if (_manager == null) return null;
        if (PreferredPlayer == MediaPlayerPreference.Any)
            return _manager.GetCurrentSession();

        var sessions = _manager.GetSessions();
        var inventory = string.Join(", ", sessions.Select(session => session.SourceAppUserModelId));
        if (logInventory || !string.Equals(inventory, _lastSessionInventory, StringComparison.Ordinal))
        {
            AppLog.Info("SMTC", $"sessions=[{inventory}]");
            _lastSessionInventory = inventory;
        }

        return PreferredPlayer == MediaPlayerPreference.AppleMusic
            ? sessions.FirstOrDefault(session => IsAppleMusicAppId(session.SourceAppUserModelId))
            : sessions.FirstOrDefault(session => IsSpotifyAppId(session.SourceAppUserModelId));
    }

    internal static bool IsAppleMusicAppId(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) &&
        (appId.Contains("AppleInc.AppleMusicWin", StringComparison.OrdinalIgnoreCase) ||
         appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase));

    internal static bool IsSpotifyAppId(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;
        return appId.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) ||
               appId.Contains("SpotifyAB.SpotifyMusic", StringComparison.OrdinalIgnoreCase);
    }

    private void ReevaluateSession(bool allowGrace, bool logInventory)
    {
        if (_disposed) return;
        GlobalSystemMediaTransportControlsSession? selected;
        try
        {
            selected = SelectPreferredSession(logInventory);
        }
        catch (Exception ex)
        {
            // 枚举失败通常是播放器重建 SMTC 的瞬态；保留旧状态等待 watchdog。
            AppLog.Error("SMTC-Select", ex);
            return;
        }

        if (selected != null)
        {
            _sessionMissingSince = null;
            if (ReferenceEquals(selected, _session))
            {
                SetHasMediaSession(true);
                return;
            }
            Attach(selected);
            return;
        }

        if (allowGrace && _session != null)
        {
            _sessionMissingSince ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - _sessionMissingSince < SessionLossGrace)
            {
                AppLog.Info("SMTC", "preferred session temporarily missing; retaining current lyrics during grace period");
                return;
            }
        }

        _sessionMissingSince = null;
        Attach(null);
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _session))
        {
            SetHasMediaSession(session != null);
            if (session != null) _ = RefreshTrackAsync("same-session-attach");
            return;
        }

        AppLog.Info("SMTC", $"attach source={session?.SourceAppUserModelId ?? "<none>"}");
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = session;
        _sessionRevision++;
        _lastTrack = null;
        _positionStabilizer.Reset();
        Interlocked.Exchange(ref _refreshPending, 0);

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        SetHasMediaSession(session != null);
        if (session != null) _ = RefreshTrackAsync("attach");
    }

    private void SetHasMediaSession(bool has)
    {
        if (HasMediaSession == has) return;
        HasMediaSession = has;
        MediaSessionAvailabilityChanged?.Invoke(has);
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => ReevaluateSession(allowGrace: true, logInventory: true);

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => ReevaluateSession(allowGrace: true, logInventory: true);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => _ = RefreshTrackAsync("metadata-event");

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        RefreshPlaybackState();
        _ = RefreshTrackAsync("playback-event");
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => PullTimeline();

    private void WatchdogTick()
    {
        ReevaluateSession(allowGrace: true, logInventory: false);
        if (_session != null) _ = RefreshTrackAsync("watchdog");
    }

    private async Task RefreshTrackAsync(string reason)
    {
        if (_disposed || _session == null) return;
        if (!await _refreshGate.WaitAsync(0))
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                await RefreshTrackCoreAsync(reason);
            } while (!_disposed && Interlocked.Exchange(ref _refreshPending, 0) != 0);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshTrackCoreAsync(string reason)
    {
        var session = _session;
        var revision = _sessionRevision;
        if (session == null) return;

        try
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
            for (var attempt = 0; attempt <= EmptyMetadataRetryDelays.Length; attempt++)
            {
                props = await session.TryGetMediaPropertiesAsync();
                if (!ReferenceEquals(_session, session) || revision != _sessionRevision) return;
                if (props != null && !string.IsNullOrWhiteSpace(props.Title)) break;
                if (attempt == EmptyMetadataRetryDelays.Length) break;
                await Task.Delay(EmptyMetadataRetryDelays[attempt]);
            }

            if (props == null || string.IsNullOrWhiteSpace(props.Title))
            {
                AppLog.Info("SMTC", $"metadata empty after retries; reason={reason}; source={session.SourceAppUserModelId}");
                return;
            }

            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var sourceAppId = session.SourceAppUserModelId;
            var rawAlbum = !string.IsNullOrWhiteSpace(props.AlbumTitle) ? props.AlbumTitle : props.AlbumArtist ?? "";
            var normalized = IsAppleMusicAppId(sourceAppId)
                ? AppleMusicMetadataNormalizer.Normalize(props.Artist, rawAlbum)
                : (props.Artist?.Trim() ?? "", rawAlbum.Trim());

            var track = new MediaTrackInfo
            {
                Title = props.Title.Trim(),
                Artist = normalized.Item1,
                Album = normalized.Item2,
                Duration = timeline.EndTime,
                AppId = sourceAppId,
                PositionMs = EstimatePositionMs(timeline, playback),
                IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                ArtworkData = _lastTrack?.ArtworkData
            };

            var changed = _lastTrack == null ||
                          !string.Equals(_lastTrack.Title, track.Title, StringComparison.Ordinal) ||
                          !string.Equals(_lastTrack.Artist, track.Artist, StringComparison.Ordinal) ||
                          !string.Equals(_lastTrack.Album, track.Album, StringComparison.Ordinal);

            if (changed) _positionStabilizer.Reset();

            if (changed && props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    var buffer = new byte[stream.Size];
                    await stream.ReadAsync(buffer.AsBuffer(), (uint)stream.Size,
                        global::Windows.Storage.Streams.InputStreamOptions.None);
                    if (!ReferenceEquals(_session, session) || revision != _sessionRevision) return;
                    track.ArtworkData = buffer;
                }
                catch
                {
                    // 封面失败不影响歌词主链路。
                }
            }

            _lastTrack = track;
            if (changed)
            {
                AppLog.Info("SMTC", $"track title={track.Title}; artist={track.Artist}; album={track.Album}; " +
                                    $"playing={track.IsPlaying}; source={track.AppId}; reason={reason}");
            }
            // watchdog 同时承担空歌词恢复心跳。上层只在当前曲目仍无歌词且退避到期时重试，
            // 已正常显示的曲目会立即忽略，不产生重复网络请求。
            if (changed || string.Equals(reason, "watchdog", StringComparison.Ordinal))
                TrackChanged?.Invoke(track);
            PlaybackStateChanged?.Invoke(track.IsPlaying);
            EmitPosition(track.PositionMs, track.IsPlaying);
        }
        catch (Exception ex)
        {
            AppLog.Error("SMTC-Refresh", ex);
        }
    }

    private static double EstimatePositionMs(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback)
    {
        var position = timeline.Position;
        if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(30))
                position += TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds * (playback.PlaybackRate ?? 1));
        }

        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (timeline.EndTime > TimeSpan.Zero && position > timeline.EndTime) position = timeline.EndTime;
        return position.TotalMilliseconds;
    }

    private void RefreshPlaybackState()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var playback = session.GetPlaybackInfo();
            PlaybackStateChanged?.Invoke(
                playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        }
        catch
        {
            // watchdog 会恢复。
        }
    }

    private void PullTimeline()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var isPlaying = playback?.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            EmitPosition(EstimatePositionMs(timeline, playback), isPlaying);
        }
        catch
        {
            // session 重建期间的瞬态，watchdog 会重新绑定。
        }
    }

    private void EmitPosition(double positionMs, bool isPlaying)
    {
        var stabilized = _positionStabilizer.Observe(positionMs, isPlaying);
        if (stabilized.HasValue) PositionChanged?.Invoke(stabilized.Value);
    }

    public async Task PlayPauseAsync()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var playback = session.GetPlaybackInfo();
            if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await session.TryPauseAsync();
            else
                await session.TryPlayAsync();
        }
        catch
        {
            // 播放器可在调用瞬间退出。
        }
    }

    public async Task NextAsync()
    {
        var session = _session;
        if (session == null) return;
        try { await session.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousAsync()
    {
        var session = _session;
        if (session == null) return;
        try { await session.TrySkipPreviousAsync(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionRevision++;
        _timelineTimer.Stop();
        _watchdogTimer.Stop();
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
