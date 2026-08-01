using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using LyricFever.Windows.App.Views;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 系统托盘：左键单击开关设置窗口（P3 后改为 K 歌窗口），右键菜单提供设置/退出。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = CreateIcon(),
            ToolTipText = "Lyric Fever",
            ContextMenu = CreateContextMenu()
        };
        _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e)
    {
        ToggleSettingsWindow();
    }

    public void ToggleSettingsWindow()
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

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var showLyrics = new MenuItem { Header = "显示歌词窗口" };
        showLyrics.Click += (_, _) => ShowLyricsWindow();
        menu.Items.Add(showLyrics);

        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => ToggleSettingsWindow();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "退出" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    /// <summary>P3 实现：K 歌悬浮窗开关。先作为占位。</summary>
    private void ShowLyricsWindow()
    {
        ToggleSettingsWindow();
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
        note.Children.Add(new EllipseGeometry(new Point(6, 14), 2.6, 2.6)); // 符头
        note.Children.Add(new EllipseGeometry(new Point(12.5, 10), 2.6, 2.6));
        note.Children.Add(new RectangleGeometry(new Rect(8.3, 4.2, 1.4, 9.2))); // 右符杆
        note.Children.Add(new RectangleGeometry(new Rect(1.9, 8.2, 1.4, 9.2))); // 左符杆
        note.Children.Add(new LineGeometry(new Point(3.3, 8.2), new Point(9.7, 7.0))); // 横梁
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
