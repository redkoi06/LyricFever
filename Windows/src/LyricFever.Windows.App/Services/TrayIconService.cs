using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using LyricFever.Windows.App.ViewModels;
using LyricFever.Windows.App.Views;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 系统托盘：左键单击开关 K 歌窗口（P3），右键菜单提供播放控制/登录/设置/退出。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private SpotifyLoginWindow? _loginWindow;
    private KaraokeWindow? _karaokeWindow;

    public TrayIconService(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = CreateIcon(),
            ToolTipText = "Lyric Fever",
            ContextMenu = CreateContextMenu()
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => ToggleLyricsWindow();
        ShowLyricsWindow();
    }

    public void ShowLyricsWindow()
    {
        if (_karaokeWindow == null)
        {
            _karaokeWindow = new KaraokeWindow(_viewModel);
            _karaokeWindow.Closed += (_, _) => _karaokeWindow = null;
        }

        if (!_karaokeWindow.IsVisible)
            _karaokeWindow.Show();
    }

    private void ToggleLyricsWindow()
    {
        if (_karaokeWindow == null)
        {
            _karaokeWindow = new KaraokeWindow(_viewModel);
            _karaokeWindow.Closed += (_, _) => _karaokeWindow = null;
        }

        if (_karaokeWindow.IsVisible)
        {
            _karaokeWindow.Hide();
        }
        else
        {
            ShowLyricsWindow();
        }
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

        var prev = new MenuItem { Header = "上一首" };
        prev.Click += async (_, _) => await _viewModel.PreviousAsync();
        menu.Items.Add(prev);

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

    private static ImageSource CreateIcon()
    {
        // 简易音符图标（白色音符 + 深色底），避免依赖 .ico 资源文件
        var drawing = new DrawingGroup();
        var bg = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromRgb(30, 30, 34)),
            Geometry = new EllipseGeometry(new Point(9, 9), 9, 9)
        };
        drawing.Children.Add(bg);

        var note = new GeometryGroup();
        note.Children.Add(new EllipseGeometry(new Point(6, 14), 2.6, 2.6));
        note.Children.Add(new EllipseGeometry(new Point(12.5, 10), 2.6, 2.6));
        note.Children.Add(new RectangleGeometry(new Rect(8.3, 4.2, 1.4, 9.2)));
        note.Children.Add(new RectangleGeometry(new Rect(1.9, 8.2, 1.4, 9.2)));
        note.Children.Add(new LineGeometry(new Point(3.3, 8.2), new Point(9.7, 7.0)));
        var noteDrawing = new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = note
        };
        drawing.Children.Add(noteDrawing);

        return new DrawingImage(drawing);
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
