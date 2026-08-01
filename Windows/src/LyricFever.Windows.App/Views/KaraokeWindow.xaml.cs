using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LyricFever.Core.Lyrics;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.ViewModels;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// K 歌悬浮窗（对应 macOS FloatingPanel + KaraokeView）：
/// 无边框透明置顶 + WS_EX_NOACTIVATE（点击不抢焦点）+ 手动拖动，三层歌词逐行高亮。
/// </summary>
public partial class KaraokeWindow : Window
{
    private const int WsExNoActivate = 0x08000000;
    private const int GwExStyle = -20;

    private readonly MainViewModel _viewModel;
    private bool _dragging;
    private Point _dragStart;
    private Point _windowStart;

    // 歌词行 UI 模型（三层排版）
    private sealed record LyricRowView(
        string Line, string Romanized, string Translated,
        Visibility RomanizedVisibility, Visibility TranslatedVisibility);

    public KaraokeWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        // 无边框透明窗口不抢焦点（对应 NSPanel .nonactivatingPanel）
        SourceInitialized += (_, _) => ApplyNoActivateStyle();

        _viewModel.LyricsStateChanged += OnLyricsStateChanged;
        _viewModel.IndexChanged += OnIndexChanged;
        _viewModel.BackgroundColorChanged += OnBackgroundColorChanged;
        Closed += (_, _) =>
        {
            _viewModel.LyricsStateChanged -= OnLyricsStateChanged;
            _viewModel.IndexChanged -= OnIndexChanged;
            _viewModel.BackgroundColorChanged -= OnBackgroundColorChanged;
        };
    }

    private void ApplyNoActivateStyle()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwExStyle);
        SetWindowLong(handle, GwExStyle, exStyle | WsExNoActivate);
    }

    // ---- 歌词渲染 ----

    private void OnLyricsStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            var lyrics = _viewModel.CurrentlyPlayingLyrics;
            var translated = _viewModel.TranslatedLyrics;
            var romanized = _viewModel.RomanizedLyrics;
            var showTranslation = AppSettings.Current.TranslateEnabled;
            var showRomanization = AppSettings.Current.RomanizationEnabled;

            if (lyrics == null || lyrics.Count == 0)
            {
                LyricsList.ItemsSource = new List<LyricRowView>
                {
                    new("♫", "", "", Visibility.Visible, Visibility.Collapsed)
                };
                return;
            }

            var rows = new List<LyricRowView>(lyrics.Count);
            for (var i = 0; i < lyrics.Count; i++)
            {
                var line = lyrics[i];
                var isPlaceholder = string.IsNullOrWhiteSpace(line.Words);
                rows.Add(new LyricRowView(
                    isPlaceholder ? "♫" : line.Words,
                    SafeAt(romanized, i),
                    SafeAt(translated, i),
                    showRomanization && !isPlaceholder ? Visibility.Visible : Visibility.Collapsed,
                    showTranslation && !isPlaceholder ? Visibility.Visible : Visibility.Collapsed));
            }
            LyricsList.ItemsSource = rows;
            UpdateHighlight();
        });
    }

    private static string SafeAt(List<string> list, int index) =>
        index >= 0 && index < list.Count ? list[index] : "";

    private void OnIndexChanged() => Dispatcher.Invoke(UpdateHighlight);

    private void UpdateHighlight()
    {
        var index = _viewModel.CurrentlyPlayingLyricsIndex;
        var fontSize = AppSettings.Current.KaraokeFontSize;

        for (var i = 0; i < LyricsList.Items.Count; i++)
        {
            var container = LyricsList.ItemContainerGenerator.ContainerFromIndex(i);
            var panel = FindVisualChild<StackPanel>(container as DependencyObject);
            var main = panel?.Children.OfType<TextBlock>().FirstOrDefault();
            if (main == null) continue;

            var isCurrent = i == index;
            main.FontSize = isCurrent ? fontSize + 4 : fontSize - 2;
            main.FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal;
            main.Foreground = new SolidColorBrush(Color.FromArgb(
                (byte)(isCurrent ? 255 : 150), 255, 255, 255));
        }

        // 滚动到当前行（居中）
        if (index is >= 0 && index < LyricsList.Items.Count)
        {
            var container = LyricsList.ItemContainerGenerator.ContainerFromIndex(index.Value);
            if (container is FrameworkElement fe)
            {
                fe.BringIntoView(new Rect(0, fe.RenderSize.Height * 0.4, 0, fe.RenderSize.Height * 0.6));
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    // ---- 背景色 ----

    private void OnBackgroundColorChanged(Color color)
    {
        Dispatcher.Invoke(() =>
        {
            var opacity = AppSettings.Current.KaraokeOpacity;
            var brush = AppSettings.Current.KaraokeUseBackgroundColor
                ? new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B))
                : new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 20, 20, 26));
            RootBorder.Background = brush;
        });
    }

    // ---- 拖动（NOACTIVATE 窗口不能用 DragMove，手动实现） ----

    private void OnBorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _windowStart = new Point(Left, Top);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnBorderMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        Left = _windowStart.X + (pos.X - _dragStart.X);
        Top = _windowStart.Y + (pos.Y - _dragStart.Y);
    }

    private void OnBorderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        SnapToScreenEdge();
    }

    /// <summary>边缘吸附（对应 macOS FloatingPanel 的吸附逻辑简化版）。</summary>
    private void SnapToScreenEdge()
    {
        var workArea = SystemParameters.WorkArea;
        const double threshold = 24;

        if (Left < workArea.Left + threshold) Left = workArea.Left;
        if (Top < workArea.Top + threshold) Top = workArea.Top;
        if (Left + Width > workArea.Right - threshold) Left = workArea.Right - Width;
        if (Top + Height > workArea.Bottom - threshold) Top = workArea.Bottom - Height;
    }

    // ---- 右键菜单 ----

    private void OnHideClicked(object sender, RoutedEventArgs e) => Hide();

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow();
        window.Show();
    }

    private void OnQuitClicked(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        OnLyricsStateChanged();
        OnBackgroundColorChanged(_viewModel.BackgroundColor);
    }

    // ---- P/Invoke ----

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
