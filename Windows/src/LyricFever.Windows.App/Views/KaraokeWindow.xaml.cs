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
    private bool _pointerDown;
    private bool _dragging;
    private NativePoint _dragStartScreen;
    private double _dragDpiScaleX = 1;
    private double _dragDpiScaleY = 1;
    private Point _windowStart;
    private Size? _pendingCardSize;
    private VerticalResizeAnchor _verticalResizeAnchor = VerticalResizeAnchor.Top;

    public event Action? HideRequested;

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

        var fontSize = Math.Clamp(AppSettings.Current.KaraokeFontSize, 12, 48);
        if (primary == null)
        {
            PlaceholderNote.FontSize = fontSize * 1.4;
            PlaceholderNote.Visibility = Visibility.Visible;
            LyricContent.Visibility = Visibility.Collapsed;
            ResizeCard(fontSize, null, null, null);
            return;
        }

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
        ResizeCard(fontSize, primary, romanized, translated);
    }

    /// <summary>
    /// 根据当前三层文字和字号计算卡片尺寸。每个可见层严格保持单行，卡片宽度
    /// 取日文、罗马音与人工译词中最长一行；只有超过屏幕安全宽度时才截断显示。
    /// </summary>
    private void ResizeCard(double fontSize, string? primary, string? romanized, string? translated)
    {
        if (!IsLoaded) return;

        var workArea = SystemParameters.WorkArea;
        // XAML 左右各 20、上下各 12，再给边框、阴影和 DPI 像素舍入留出余量。
        const double horizontalPadding = 42;
        const double verticalPadding = 30;
        var maximumContentWidth = Math.Max(220, Math.Min(1060, workArea.Width - 96) - horizontalPadding);

        if (primary == null)
        {
            ApplyCardSize(Math.Clamp(fontSize * 8 + horizontalPadding, 220, 460),
                Math.Clamp(fontSize * 2.8 + verticalPadding, 72, 170));
            return;
        }

        var rawWidth = Math.Max(
            MeasureUnwrappedText(primary, PrimaryLine, fontSize).Width,
            Math.Max(
                MeasureUnwrappedText(romanized, RomanizedLine, fontSize * 0.70).Width,
                MeasureUnwrappedText(translated, TranslatedLine, fontSize * 0.82).Width));
        var minimumContentWidth = Math.Clamp(fontSize * 7, 150, 280);
        // 留半个字号的宽度余量，避免 DPI 取整把最后一个日文字挤到下一行或裁掉。
        var contentWidth = Math.Clamp(Math.Ceiling(rawWidth + Math.Max(8, fontSize * 0.5)),
            minimumContentWidth, maximumContentWidth);

        PrimaryLine.MaxWidth = contentWidth;
        RomanizedLine.MaxWidth = contentWidth;
        TranslatedLine.MaxWidth = contentWidth;

        // 使用真实 TextBlock 布局高度，包含字体 fallback、三层 Margin 与系统 DPI；
        // 这比单独的 FormattedText 更能避免底部人工译词被窗口边界裁切。
        LyricContent.InvalidateMeasure();
        LyricContent.Measure(new Size(contentWidth, double.PositiveInfinity));
        var desiredHeight = Math.Ceiling(LyricContent.DesiredSize.Height) + verticalPadding;

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(10 + fontSize * 0.25, 14, 22));
        ApplyCardSize(contentWidth + horizontalPadding,
            Math.Clamp(desiredHeight, 68, Math.Max(68, workArea.Height - 96)));
    }

    private static Size MeasureUnwrappedText(string? text,
        System.Windows.Controls.TextBlock source, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Size(0, 0);
        var probe = new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontFamily = source.FontFamily,
            FontStyle = source.FontStyle,
            FontWeight = source.FontWeight,
            FontStretch = source.FontStretch,
            FontSize = fontSize,
            FlowDirection = source.FlowDirection,
            TextWrapping = TextWrapping.NoWrap
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize;
    }

    private void ApplyCardSize(double newWidth, double newHeight)
    {
        if (_pointerDown)
        {
            // 歌词可能在拖动过程中切行；延迟改变窗口尺寸，避免鼠标基准与窗口边界同时跳动。
            _pendingCardSize = new Size(newWidth, newHeight);
            return;
        }
        ApplyCardSizeImmediately(newWidth, newHeight);
    }

    private void ApplyCardSizeImmediately(double newWidth, double newHeight)
    {
        if (Math.Abs(Width - newWidth) < 0.5 && Math.Abs(Height - newHeight) < 0.5) return;

        var oldWidth = ActualWidth > 0 ? ActualWidth : Width;
        var oldHeight = ActualHeight > 0 ? ActualHeight : Height;
        var oldBottom = Top + oldHeight;
        var oldCenterX = Left + oldWidth / 2;
        var oldCenterY = Top + oldHeight / 2;

        Width = newWidth;
        Height = newHeight;

        if (!double.IsNaN(Left))
            Left = oldCenterX - newWidth / 2;
        if (!double.IsNaN(Top))
        {
            Top = _verticalResizeAnchor switch
            {
                VerticalResizeAnchor.Top => Top,
                VerticalResizeAnchor.Bottom => oldBottom - newHeight,
                _ => oldCenterY - newHeight / 2
            };
        }
        KeepInsideWorkArea();
    }

    private void ApplyPendingCardSize()
    {
        if (_pendingCardSize is not { } pending) return;
        _pendingCardSize = null;
        ApplyCardSizeImmediately(pending.Width, pending.Height);
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
        var opacity = Math.Clamp(AppSettings.Current.KaraokeOpacity, 0.78, 1.0);
        var requestedColor = AppSettings.Current.KaraokeUseBackgroundColor
            ? color
            : Color.FromRgb(45, 60, 204);
        var baseColor = EnsureDarkBackground(requestedColor);
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(255 * opacity), baseColor.R, baseColor.G, baseColor.B));
    }

    private static Color EnsureDarkBackground(Color color)
    {
        // 专辑主色可能接近白色或亮黄色；压到稳定的深色亮度，保证三层白字和阴影可读。
        const double maximumLuma = 48;
        var luma = color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;
        if (luma <= maximumLuma) return Color.FromRgb(color.R, color.G, color.B);

        var scale = maximumLuma / luma;
        return Color.FromRgb(
            (byte)Math.Round(color.R * scale),
            (byte)Math.Round(color.G * scale),
            (byte)Math.Round(color.B * scale));
    }

    // ---- 拖动（NOACTIVATE 窗口不能用 DragMove，手动实现） ----

    private void OnBorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _pointerDown = true;
        _dragging = false;
        if (!GetCursorPos(out _dragStartScreen))
        {
            _pointerDown = false;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        _dragDpiScaleX = Math.Max(0.1, dpi.DpiScaleX);
        _dragDpiScaleY = Math.Max(0.1, dpi.DpiScaleY);
        _windowStart = new Point(Left, Top);
        if (!CaptureMouse())
        {
            _pointerDown = false;
            return;
        }
        AppLog.Info("Drag", $"pointer down; left={Left:F1}; top={Top:F1}");
        e.Handled = true;
    }

    private void OnBorderMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown) return;
        if (!GetCursorPos(out var cursor)) return;
        var deltaX = (cursor.X - _dragStartScreen.X) / _dragDpiScaleX;
        var deltaY = (cursor.Y - _dragStartScreen.Y) / _dragDpiScaleY;
        if (!_dragging)
        {
            if (Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
                return;
            _dragging = true;
            AppLog.Info("Drag", "drag threshold reached");
        }

        // 使用固定屏幕坐标差，并对齐到整数 DIP，避免移动窗口自身坐标造成反馈抖动和半像素闪烁。
        Left = Math.Round(_windowStart.X + deltaX, MidpointRounding.AwayFromZero);
        Top = Math.Round(_windowStart.Y + deltaY, MidpointRounding.AwayFromZero);
        e.Handled = true;
    }

    private void OnBorderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_pointerDown) return;
        var didDrag = _dragging;
        _pointerDown = false;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        ApplyPendingCardSize();
        if (!didDrag)
        {
            e.Handled = true;
            return;
        }

        SnapToScreenEdge();
        UpdateVerticalResizeAnchor();
        AppSettings.Current.KaraokeLeft = Left;
        AppSettings.Current.KaraokeTop = Top;
        AppSettings.Current.KaraokeCenterX = Left + Width / 2;
        AppSettings.Current.Save();
        AppLog.Info("Drag", $"drag completed; left={Left:F1}; top={Top:F1}; " +
                            $"centerX={AppSettings.Current.KaraokeCenterX:F1}");
        e.Handled = true;
    }

    private void CancelPointerGesture()
    {
        _pointerDown = false;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        ApplyPendingCardSize();
    }

    private void OnBorderLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_pointerDown && !IsMouseCaptured)
        {
            AppLog.Info("Drag", "mouse capture lost; gesture cancelled safely");
            CancelPointerGesture();
        }
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

    private void KeepInsideWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void UpdateVerticalResizeAnchor()
    {
        var workArea = SystemParameters.WorkArea;
        var centerY = Top + Height / 2;
        _verticalResizeAnchor = centerY < workArea.Top + workArea.Height / 3
            ? VerticalResizeAnchor.Top
            : centerY > workArea.Top + workArea.Height * 2 / 3
                ? VerticalResizeAnchor.Bottom
                : VerticalResizeAnchor.Center;
    }

    // ---- 右键菜单 ----

    private void OnHideClicked(object sender, RoutedEventArgs e)
    {
        if (HideRequested == null) Hide();
        else HideRequested.Invoke();
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow();
        window.Show();
    }

    private void OnQuitClicked(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RestorePosition();
        UpdateVerticalResizeAnchor();
        RenderCurrentLyric();
        ApplyBackground(_viewModel.BackgroundColor);
    }

    private void RestorePosition()
    {
        var workArea = SystemParameters.WorkArea;
        var savedLeft = AppSettings.Current.KaraokeLeft;
        var savedTop = AppSettings.Current.KaraokeTop;
        var savedCenterX = AppSettings.Current.KaraokeCenterX ??
                           (savedLeft.HasValue ? savedLeft.Value + Width / 2 : null);

        if (savedCenterX.HasValue && savedTop.HasValue &&
            savedCenterX.Value > workArea.Left + 40 && savedCenterX.Value < workArea.Right - 40 &&
            savedTop.Value < workArea.Bottom - 40 && savedTop.Value + Height > workArea.Top + 40)
        {
            Left = savedCenterX.Value - Width / 2;
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum VerticalResizeAnchor
    {
        Top,
        Center,
        Bottom
    }
}
