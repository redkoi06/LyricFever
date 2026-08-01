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
/// 读取任意 SMTC 集成应用（Spotify、Apple Music 等）的播放状态、曲目与进度，并支持播放控制。
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

    public bool IsRunning { get; private set; }

    public MediaSessionWatcher()
    {
        _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timelineTimer.Tick += async (_, _) => await PullTimelineAsync();
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

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
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

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        Attach(_manager?.GetCurrentSession());
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await RefreshTrackAsync();

    private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => await RefreshPlaybackStateAsync();

    private async void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => await PullTimelineAsync();

    private async Task RefreshTrackAsync()
    {
        if (_session == null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (props == null) return;

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

    private async Task RefreshPlaybackStateAsync()
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

    private async Task PullTimelineAsync()
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
