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
    private KaraokeWindow? _karaokeWindow;
    private MenuItem? _lyricsMenuItem;
    private MenuItem? _refreshMenuItem;
    private TextBlock? _trayStatusText;
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
        UpdateTrayPresentation();
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
        UpdateTrayPresentation();
        SyncLyricsWindowVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.IsPlaying) or
            nameof(MainViewModel.HasMediaSession) or nameof(MainViewModel.IsFetching)))
            return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            UpdateTrayPresentation();
            if (e.PropertyName is nameof(MainViewModel.IsPlaying) or nameof(MainViewModel.HasMediaSession))
                SyncLyricsWindowVisibility();
        });
    }

    private void SyncLyricsWindowVisibility()
    {
        var shouldShow = _userWantsLyricsWindow && _viewModel.HasMediaSession && _viewModel.IsPlaying;

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
        var shouldShow = _userWantsLyricsWindow && _viewModel.HasMediaSession && _viewModel.IsPlaying;
        ApplyLyricsWindowVisibility(shouldShow);
    }

    private void ApplyLyricsWindowVisibility(bool shouldShow)
    {
        if (_lastWindowVisibility != shouldShow)
        {
            AppLog.Info("Tray", $"lyricsWindowVisible={shouldShow}; userEnabled={_userWantsLyricsWindow}; " +
                                $"hasSession={_viewModel.HasMediaSession}; isPlaying={_viewModel.IsPlaying}");
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
        if (_settingsWindow?.IsVisible == true)
        {
            _settingsWindow.Hide();
            return;
        }

        ShowSettingsWindow();
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (!_settingsWindow.IsVisible) _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private ContextMenu CreateContextMenu()
    {
        var menuItemStyle = (Style)Application.Current.FindResource("TrayMenuItemStyle");
        var separatorStyle = (Style)Application.Current.FindResource("TraySeparatorStyle");
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("TrayContextMenuStyle")
        };

        _trayStatusText = new TextBlock
        {
            Text = "正在检测 Apple Music",
            FontSize = 10.5,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush")
        };
        var statusHeader = new MenuItem
        {
            Style = menuItemStyle,
            Focusable = false,
            IsHitTestVisible = false,
            Padding = new Thickness(10, 8, 10, 10),
            Icon = new Image
            {
                Source = LoadOriginalAppIcon(),
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform
            },
            Header = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Lyric Fever",
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush")
                    },
                    _trayStatusText
                }
            }
        };
        menu.Items.Add(statusHeader);
        menu.Items.Add(new Separator { Style = separatorStyle });

        _lyricsMenuItem = CreateMenuItem("隐藏歌词窗口", "\uE890", menuItemStyle);
        _lyricsMenuItem.Click += (_, _) => ToggleLyricsWindow();
        menu.Items.Add(_lyricsMenuItem);

        _refreshMenuItem = CreateMenuItem("重新获取当前歌词", "\uE72C", menuItemStyle);
        _refreshMenuItem.Click += (_, _) => _viewModel.RefreshLyrics();
        menu.Items.Add(_refreshMenuItem);
        menu.Items.Add(new Separator { Style = separatorStyle });

        var settings = CreateMenuItem("设置", "\uE713", menuItemStyle);
        settings.Click += (_, _) => ToggleSettingsWindow();
        menu.Items.Add(settings);

        var quit = CreateMenuItem("退出 Lyric Fever", "\uE7E8", menuItemStyle);
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, string glyph, Style style) => new()
    {
        Header = header,
        Style = style,
        Icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private void UpdateTrayPresentation()
    {
        if (_trayStatusText != null)
        {
            if (!_viewModel.HasMediaSession)
            {
                _trayStatusText.Text = "等待 Apple Music";
                _trayStatusText.Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush");
            }
            else if (_viewModel.IsFetching)
            {
                _trayStatusText.Text = "正在获取网络歌词";
                _trayStatusText.Foreground = (Brush)Application.Current.FindResource("AccentBrush");
            }
            else
            {
                _trayStatusText.Text = _viewModel.IsPlaying
                    ? "Apple Music · 字幕同步中"
                    : "Apple Music · 已连接";
                _trayStatusText.Foreground = (Brush)Application.Current.FindResource("AccentBrush");
            }
        }

        if (_lyricsMenuItem != null)
            _lyricsMenuItem.Header = _userWantsLyricsWindow ? "隐藏歌词窗口" : "显示歌词窗口";
        if (_refreshMenuItem != null)
            _refreshMenuItem.IsEnabled = _viewModel.HasMediaSession && !_viewModel.IsFetching;
        if (_trayIcon != null)
            _trayIcon.ToolTipText = _viewModel.HasMediaSession
                ? "Lyric Fever · Apple Music"
                : "Lyric Fever · 等待 Apple Music";
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
