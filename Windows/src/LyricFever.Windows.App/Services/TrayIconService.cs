using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using LyricFever.Windows.App.ViewModels;
using LyricFever.Windows.App.Views;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 系统托盘与字幕窗口生命周期。正式运行时字幕只在媒体 session 正在播放时可见；
/// 暂停、停止或 session 消失会隐藏，恢复播放后按用户开关重新显示。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private SpotifyLoginWindow? _loginWindow;
    private KaraokeWindow? _karaokeWindow;
    private readonly DispatcherTimer _hideDelayTimer;
    private bool _userWantsLyricsWindow = true;
    private bool? _lastWindowVisibility;

    public TrayIconService(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _hideDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _hideDelayTimer.Tick += OnHideDelayElapsed;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = LoadOriginalAppIcon(),
            ToolTipText = "Lyric Fever",
            ContextMenu = CreateContextMenu()
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => ToggleLyricsWindow();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncLyricsWindowVisibility();
    }

    public void ShowLyricsWindow()
    {
        _userWantsLyricsWindow = true;
        SyncLyricsWindowVisibility();
    }

    private void ToggleLyricsWindow()
    {
        _userWantsLyricsWindow = !_userWantsLyricsWindow;
        SyncLyricsWindowVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.IsPlaying) or nameof(MainViewModel.HasMediaSession)))
            return;
        Application.Current.Dispatcher.BeginInvoke(SyncLyricsWindowVisibility);
    }

    private void SyncLyricsWindowVisibility()
    {
        var shouldShow = App.CurrentApp.IsVisualInspectionMode ||
                          (_userWantsLyricsWindow && _viewModel.HasMediaSession && _viewModel.IsPlaying);

        if (shouldShow)
        {
            _hideDelayTimer.Stop();
            ApplyLyricsWindowVisibility(true);
            return;
        }

        // Apple Music can briefly report Paused while its SMTC timeline is refreshed.
        // Keep an already visible card alive across that transient state, but preserve
        // immediate hiding for explicit user actions and confirmed session loss.
        var shouldDebouncePlaybackPause = _userWantsLyricsWindow &&
                                           _viewModel.HasMediaSession &&
                                           !_viewModel.IsPlaying &&
                                           _karaokeWindow?.IsVisible == true;
        if (shouldDebouncePlaybackPause)
        {
            if (!_hideDelayTimer.IsEnabled) _hideDelayTimer.Start();
            return;
        }

        _hideDelayTimer.Stop();
        ApplyLyricsWindowVisibility(false);
    }

    private void OnHideDelayElapsed(object? sender, EventArgs e)
    {
        _hideDelayTimer.Stop();
        var shouldShow = App.CurrentApp.IsVisualInspectionMode ||
                         (_userWantsLyricsWindow && _viewModel.HasMediaSession && _viewModel.IsPlaying);
        ApplyLyricsWindowVisibility(shouldShow);
    }

    private void ApplyLyricsWindowVisibility(bool shouldShow)
    {
        if (_lastWindowVisibility != shouldShow)
        {
            AppLog.Info("Tray", $"lyricsWindowVisible={shouldShow}; userEnabled={_userWantsLyricsWindow}; " +
                                $"hasSession={_viewModel.HasMediaSession}; isPlaying={_viewModel.IsPlaying}; " +
                                $"visualInspection={App.CurrentApp.IsVisualInspectionMode}");
            _lastWindowVisibility = shouldShow;
        }
        if (!shouldShow)
        {
            _karaokeWindow?.Hide();
            return;
        }

        EnsureKaraokeWindow();
        if (_karaokeWindow is { IsVisible: false }) _karaokeWindow.Show();
    }

    private void EnsureKaraokeWindow()
    {
        if (_karaokeWindow != null) return;
        _karaokeWindow = new KaraokeWindow(_viewModel);
        _karaokeWindow.HideRequested += OnLyricsHideRequested;
        _karaokeWindow.Closed += (_, _) =>
        {
            if (_karaokeWindow != null)
                _karaokeWindow.HideRequested -= OnLyricsHideRequested;
            _karaokeWindow = null;
        };
    }

    private void OnLyricsHideRequested()
    {
        _userWantsLyricsWindow = false;
        SyncLyricsWindowVisibility();
    }

    private void ToggleSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else if (_settingsWindow.IsVisible)
        {
            _settingsWindow.Hide();
        }
        else
        {
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
    }

    private void ShowLoginWindow()
    {
        if (_loginWindow == null)
        {
            _loginWindow = new SpotifyLoginWindow();
            _loginWindow.Closed += (_, _) => _loginWindow = null;
        }
        _loginWindow.Show();
        _loginWindow.Activate();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var lyrics = new MenuItem { Header = "显示歌词窗口" };
        lyrics.Click += (_, _) => ToggleLyricsWindow();
        menu.Items.Add(lyrics);
        menu.Items.Add(new Separator());

        var playPause = new MenuItem { Header = "播放 / 暂停" };
        playPause.Click += async (_, _) => await _viewModel.PlayPauseAsync();
        menu.Items.Add(playPause);

        var previous = new MenuItem { Header = "上一首" };
        previous.Click += async (_, _) => await _viewModel.PreviousAsync();
        menu.Items.Add(previous);

        var next = new MenuItem { Header = "下一首" };
        next.Click += async (_, _) => await _viewModel.NextAsync();
        menu.Items.Add(next);

        var refresh = new MenuItem { Header = "刷新歌词" };
        refresh.Click += (_, _) => _viewModel.RefreshLyrics();
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());

        var login = new MenuItem { Header = "Spotify 登录" };
        login.Click += (_, _) => ShowLoginWindow();
        menu.Items.Add(login);

        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => ToggleSettingsWindow();
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "退出" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);
        return menu;
    }

    private static ImageSource LoadOriginalAppIcon()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(
            "pack://application:,,,/Assets/LyricFeverTray.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _hideDelayTimer.Stop();
        _hideDelayTimer.Tick -= OnHideDelayElapsed;
        if (_karaokeWindow != null)
            _karaokeWindow.HideRequested -= OnLyricsHideRequested;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
