using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.ViewModels;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// K 歌悬浮窗（对应 macOS FloatingPanel + KaraokeView）：
/// 无边框透明置顶 + WS_EX_NOACTIVATE（点击不抢焦点）+ 手动拖动。
/// 与 Swift KaraokeView 一致，仅显示当前一句、可选罗马音/翻译；没有可显示歌词时显示音符。
/// </summary>
public partial class KaraokeWindow : Window
{
    private const int WsExNoActivate = 0x08000000;
    private const int GwExStyle = -20;

    private readonly MainViewModel _viewModel;
    private bool _dragging;
    private Point _dragStart;
    private Point _windowStart;

    public KaraokeWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        // 正常运行不抢焦点；视觉检查模式保留可定位窗口，便于自动截图验收。
        if (App.CurrentApp.IsVisualInspectionMode)
        {
            ShowInTaskbar = true;
            ShowActivated = true;
        }
        else
        {
            SourceInitialized += (_, _) => ApplyNoActivateStyle();
        }

        _viewModel.LyricsStateChanged += OnLyricsStateChanged;
        _viewModel.IndexChanged += OnIndexChanged;
        _viewModel.BackgroundColorChanged += OnBackgroundColorChanged;
        AppSettings.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) =>
        {
            _viewModel.LyricsStateChanged -= OnLyricsStateChanged;
            _viewModel.IndexChanged -= OnIndexChanged;
            _viewModel.BackgroundColorChanged -= OnBackgroundColorChanged;
            AppSettings.SettingsChanged -= OnSettingsChanged;
        };
    }

    private void ApplyNoActivateStyle()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwExStyle);
        SetWindowLong(handle, GwExStyle, exStyle | WsExNoActivate);
    }

    // ---- 歌词渲染 ----

    private void OnLyricsStateChanged() => Dispatcher.Invoke(RenderCurrentLyric);

    private void OnIndexChanged() => Dispatcher.Invoke(RenderCurrentLyric);

    private void OnSettingsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            RenderCurrentLyric();
            ApplyBackground(_viewModel.BackgroundColor);
        });
    }

    private void RenderCurrentLyric()
    {
        var index = _viewModel.CurrentlyPlayingLyricsIndex;
        var lyrics = _viewModel.CurrentlyPlayingLyrics;
        var indexValue = index.GetValueOrDefault(-1);
        var primary = indexValue >= 0 && lyrics != null && indexValue < lyrics.Count
            ? DisplayableLyric(lyrics[indexValue].Words)
            : null;

        if (primary == null)
        {
            PlaceholderNote.Visibility = Visibility.Visible;
            LyricContent.Visibility = Visibility.Collapsed;
            return;
        }

        var fontSize = Math.Clamp(AppSettings.Current.KaraokeFontSize, 12, 48);
        var romanized = AppSettings.Current.RomanizationEnabled
            ? DisplayableLyric(SafeAt(_viewModel.RomanizedLyrics, indexValue))
            : null;
        var translated = AppSettings.Current.TranslateEnabled
            ? DisplayableLyric(SafeAt(_viewModel.TranslatedLyrics, indexValue))
            : null;

        if (string.Equals(primary, translated, StringComparison.OrdinalIgnoreCase))
            translated = null;

        PrimaryLine.Text = primary;
        PrimaryLine.FontSize = fontSize;
        RomanizedLine.Text = romanized ?? "";
        RomanizedLine.FontSize = fontSize * 0.70;
        RomanizedLine.Visibility = romanized == null ? Visibility.Collapsed : Visibility.Visible;
        TranslatedLine.Text = translated ?? "";
        TranslatedLine.FontSize = fontSize * 0.82;
        TranslatedLine.Visibility = translated == null ? Visibility.Collapsed : Visibility.Visible;

        PlaceholderNote.Visibility = Visibility.Collapsed;
        LyricContent.Visibility = Visibility.Visible;
    }

    private static string? DisplayableLyric(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.All(ch => char.IsWhiteSpace(ch) || "♪♫♩♬".Contains(ch)) ? null : trimmed;
    }

    private static string SafeAt(IReadOnlyList<string> list, int index) =>
        index >= 0 && index < list.Count ? list[index] : "";

    // ---- 背景色 ----

    private void OnBackgroundColorChanged(Color color) => Dispatcher.Invoke(() => ApplyBackground(color));

    private void ApplyBackground(Color color)
    {
        var opacity = Math.Clamp(AppSettings.Current.KaraokeOpacity, 0.05, 1.0);
        var baseColor = AppSettings.Current.KaraokeUseBackgroundColor
            ? color
            : Color.FromRgb(45, 60, 204);
        AlbumTint.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(255 * opacity), baseColor.R, baseColor.G, baseColor.B));
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
        AppSettings.Current.KaraokeLeft = Left;
        AppSettings.Current.KaraokeTop = Top;
        AppSettings.Current.Save();
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
        RestorePosition();
        RenderCurrentLyric();
        ApplyBackground(_viewModel.BackgroundColor);
    }

    private void RestorePosition()
    {
        var workArea = SystemParameters.WorkArea;
        var savedLeft = AppSettings.Current.KaraokeLeft;
        var savedTop = AppSettings.Current.KaraokeTop;

        if (savedLeft.HasValue && savedTop.HasValue &&
            savedLeft.Value < workArea.Right - 40 && savedLeft.Value + Width > workArea.Left + 40 &&
            savedTop.Value < workArea.Bottom - 40 && savedTop.Value + Height > workArea.Top + 40)
        {
            Left = savedLeft.Value;
            Top = savedTop.Value;
            return;
        }

        Left = workArea.Right - Width - 48;
        Top = workArea.Top + 48;
    }

    // ---- P/Invoke ----

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
