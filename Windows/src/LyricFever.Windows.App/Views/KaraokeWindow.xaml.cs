using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LyricFever.Core.Appearance;
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
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private readonly MainViewModel _viewModel;
    private bool _pointerDown;
    private bool _dragging;
    private NativePoint _dragStartScreen;
    private double _dragDpiScaleX = 1;
    private double _dragDpiScaleY = 1;
    private Point _windowStart;
    private LyricRenderSnapshot? _pendingLyricRender;
    private bool _hasAppliedInitialCardSize;
    private readonly DispatcherTimer _resizeCommitTimer;
    private readonly DispatcherTimer _settingsApplyTimer;
    private bool _settingsNeedsLayout;
    private bool _settingsNeedsBackground;
    private VerticalResizeAnchor _verticalResizeAnchor = VerticalResizeAnchor.Top;

    public event Action? HideRequested;

    public KaraokeWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _resizeCommitTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _resizeCommitTimer.Tick += OnResizeCommitTimerTick;
        _settingsApplyTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _settingsApplyTimer.Tick += OnSettingsApplyTimerTick;

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
            _resizeCommitTimer.Stop();
            _resizeCommitTimer.Tick -= OnResizeCommitTimerTick;
            _settingsApplyTimer.Stop();
            _settingsApplyTimer.Tick -= OnSettingsApplyTimerTick;
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

    private void OnSettingsChanged(string? propertyName)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => QueueSettingsUpdate(propertyName));
            return;
        }

        QueueSettingsUpdate(propertyName);
    }

    private void QueueSettingsUpdate(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(AppSettings.KaraokeOpacity):
            case nameof(AppSettings.KaraokeUseBackgroundColor):
                _settingsNeedsBackground = true;
                break;
            case nameof(AppSettings.KaraokeFontSize):
            case nameof(AppSettings.TranslateEnabled):
            case nameof(AppSettings.RomanizationEnabled):
                _settingsNeedsLayout = true;
                break;
            case null:
                _settingsNeedsLayout = true;
                _settingsNeedsBackground = true;
                break;
            default:
                return;
        }

        if (!_settingsApplyTimer.IsEnabled) _settingsApplyTimer.Start();
    }

    private void OnSettingsApplyTimerTick(object? sender, EventArgs e)
    {
        _settingsApplyTimer.Stop();
        var updateLayout = _settingsNeedsLayout;
        var updateBackground = _settingsNeedsBackground;
        _settingsNeedsLayout = false;
        _settingsNeedsBackground = false;

        if (updateLayout) RenderCurrentLyric();
        if (updateBackground) ApplyBackground(_viewModel.BackgroundColor);
    }

    private void RenderCurrentLyric()
    {
        var index = _viewModel.CurrentlyPlayingLyricsIndex;
        var lyrics = _viewModel.CurrentlyPlayingLyrics;
        var indexValue = index.GetValueOrDefault(-1);
        var primary = indexValue >= 0 && lyrics != null && indexValue < lyrics.Count
            ? DisplayableLyric(lyrics[indexValue].Words)
            : null;

        var fontSizes = CreateFontSizes(Math.Clamp(AppSettings.Current.KaraokeFontSize, 12, 48));
        if (primary == null)
        {
            QueueLyricRender(new LyricRenderSnapshot(fontSizes, null, null, null));
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

        QueueLyricRender(new LyricRenderSnapshot(fontSizes, primary, romanized, translated));
    }

    private void QueueLyricRender(LyricRenderSnapshot snapshot)
    {
        _pendingLyricRender = snapshot;
        if (!IsLoaded) return;

        if (!_hasAppliedInitialCardSize)
        {
            CommitPendingLyricRender();
            return;
        }

        if (_pointerDown)
        {
            // 歌词可能在拖动过程中切行；延迟整个视觉快照，避免文字或窗口边界单独跳动。
            _resizeCommitTimer.Stop();
            return;
        }

        // Source text and derived lines can notify separately. Keep the old text and old card
        // together while coalescing, then replace both from the last complete snapshot.
        _resizeCommitTimer.Stop();
        _resizeCommitTimer.Start();
    }

    private void CommitPendingLyricRender()
    {
        _resizeCommitTimer.Stop();
        if (_pendingLyricRender is not { } snapshot) return;
        _pendingLyricRender = null;

        var layout = CalculateCardLayout(snapshot.FontSizes,
            snapshot.Primary, snapshot.Romanized, snapshot.Translated);
        ApplyLyricVisuals(snapshot, layout);
        _hasAppliedInitialCardSize = true;
        ApplyCardSizeImmediately(layout.Width, layout.Height);
    }

    private void ApplyLyricVisuals(LyricRenderSnapshot snapshot, LyricCardLayout layout)
    {
        var fontSizes = snapshot.FontSizes;
        RootBorder.CornerRadius = new CornerRadius(layout.CornerRadius);
        if (snapshot.Primary == null)
        {
            PlaceholderNote.FontSize = fontSizes.Placeholder;
            PlaceholderNote.Visibility = Visibility.Visible;
            LyricContent.Visibility = Visibility.Collapsed;
            return;
        }

        PrimaryLine.Text = snapshot.Primary;
        PrimaryLine.FontSize = fontSizes.Primary;
        PrimaryLine.LineHeight = fontSizes.PrimaryLineHeight;
        RomanizedLine.Text = snapshot.Romanized ?? "";
        RomanizedLine.FontSize = fontSizes.Romanized;
        RomanizedLine.LineHeight = fontSizes.RomanizedLineHeight;
        RomanizedLine.Visibility = snapshot.Romanized == null ? Visibility.Collapsed : Visibility.Visible;
        TranslatedLine.Text = snapshot.Translated ?? "";
        TranslatedLine.FontSize = fontSizes.Translated;
        TranslatedLine.LineHeight = fontSizes.TranslatedLineHeight;
        TranslatedLine.Visibility = snapshot.Translated == null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryLine.MaxWidth = layout.ContentWidth;
        RomanizedLine.MaxWidth = layout.ContentWidth;
        TranslatedLine.MaxWidth = layout.ContentWidth;

        PlaceholderNote.Visibility = Visibility.Collapsed;
        LyricContent.Visibility = Visibility.Visible;
    }

    private LyricFontSizes CreateFontSizes(double configuredSize)
    {
        var dpiScale = Math.Max(0.1, VisualTreeHelper.GetDpi(this).DpiScaleY);
        double Snap(double value) => Math.Max(1, Math.Round(value * dpiScale) / dpiScale);
        return new LyricFontSizes(
            Primary: Snap(configuredSize),
            Romanized: Snap(configuredSize * 0.68),
            Translated: Snap(configuredSize * 0.80),
            Placeholder: Snap(configuredSize * 1.4),
            PrimaryLineHeight: Snap(configuredSize * 1.30),
            RomanizedLineHeight: Snap(configuredSize * 0.68 * 1.34),
            TranslatedLineHeight: Snap(configuredSize * 0.80 * 1.32));
    }

    /// <summary>
    /// 根据当前三层文字和字号计算卡片尺寸。每个可见层严格保持单行，卡片宽度
    /// 取日文、罗马音与人工译词中最长一行；只有超过屏幕安全宽度时才截断显示。
    /// </summary>
    private LyricCardLayout CalculateCardLayout(LyricFontSizes fontSizes,
        string? primary, string? romanized, string? translated)
    {
        var workArea = SystemParameters.WorkArea;
        // XAML 左右各 18、上下各 11，仅保留必要的圆角与 DPI 舍入余量。
        const double horizontalPadding = 38;
        const double verticalPadding = 26;
        var maximumContentWidth = Math.Max(220, Math.Min(1060, workArea.Width - 96) - horizontalPadding);

        if (primary == null)
        {
            return new LyricCardLayout(
                Math.Clamp(fontSizes.Primary * 6 + horizontalPadding, 140, 300),
                Math.Clamp(fontSizes.Primary * 2.4 + verticalPadding, 56, 130),
                0,
                Math.Clamp(10 + fontSizes.Primary * 0.25, 14, 22));
        }

        var rawWidth = Math.Max(
            MeasureUnwrappedText(primary, PrimaryLine, fontSizes.Primary).Width,
            Math.Max(
                MeasureUnwrappedText(romanized, RomanizedLine, fontSizes.Romanized).Width,
                MeasureUnwrappedText(translated, TranslatedLine, fontSizes.Translated).Width));
        var minimumContentWidth = Math.Clamp(fontSizes.Primary * 5, 80, 220);
        // 仅留少量字形与 DPI 取整余量，让短句卡片真正贴合最长一行。
        var contentWidth = Math.Clamp(Math.Ceiling(rawWidth + Math.Max(6, fontSizes.Primary * 0.35)),
            minimumContentWidth, maximumContentWidth);

        // Every visible TextBlock uses BlockLineHeight and NoWrap, so its final height is known
        // without mutating or measuring the live visual tree. Measuring offscreen first prevents
        // the Japanese source line from becoming visible one frame before the card geometry.
        var contentHeight = fontSizes.PrimaryLineHeight;
        if (romanized != null) contentHeight += 7 + fontSizes.RomanizedLineHeight;
        if (translated != null) contentHeight += 6 + fontSizes.TranslatedLineHeight;
        var desiredHeight = Math.Ceiling(contentHeight) + verticalPadding;

        return new LyricCardLayout(
            contentWidth + horizontalPadding,
            Math.Clamp(desiredHeight, 46, Math.Max(46, workArea.Height - 96)),
            contentWidth,
            Math.Clamp(10 + fontSizes.Primary * 0.25, 14, 22));
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

    private void ApplyCardSizeImmediately(double newWidth, double newHeight)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        newWidth = SnapToDevicePixel(newWidth, dpi.DpiScaleX);
        newHeight = SnapToDevicePixel(newHeight, dpi.DpiScaleY);

        var oldWidth = ActualWidth > 0 ? ActualWidth : Width;
        var oldHeight = ActualHeight > 0 ? ActualHeight : Height;
        var oldLeft = double.IsNaN(Left) ? SystemParameters.WorkArea.Left : Left;
        var oldTop = double.IsNaN(Top) ? SystemParameters.WorkArea.Top : Top;
        var oldBottom = oldTop + oldHeight;
        var oldCenterX = oldLeft + oldWidth / 2;
        var oldCenterY = oldTop + oldHeight / 2;

        var targetLeft = SnapToDevicePixel(oldCenterX - newWidth / 2, dpi.DpiScaleX);
        var targetTop = SnapToDevicePixel(_verticalResizeAnchor switch
        {
            VerticalResizeAnchor.Top => oldTop,
            VerticalResizeAnchor.Bottom => oldBottom - newHeight,
            _ => oldCenterY - newHeight / 2
        }, dpi.DpiScaleY);
        var workArea = SystemParameters.WorkArea;
        targetLeft = Math.Clamp(targetLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - newWidth));
        targetTop = Math.Clamp(targetTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - newHeight));

        if (Math.Abs(oldWidth - newWidth) < 0.5 && Math.Abs(oldHeight - newHeight) < 0.5 &&
            Math.Abs(oldLeft - targetLeft) < 0.5 && Math.Abs(oldTop - targetTop) < 0.5)
            return;

        ApplyWindowGeometryAtomically(
            oldLeft, oldTop,
            targetLeft, targetTop,
            newWidth, newHeight,
            dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void ApplyWindowGeometryAtomically(
        double oldLeft, double oldTop,
        double targetLeft, double targetTop,
        double targetWidth, double targetHeight,
        double dpiScaleX, double dpiScaleY)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var appliedNatively = false;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var currentRect))
        {
            // Setting Width/Height/Left/Top separately lets WPF issue multiple HWND moves.
            // On a 60-Hz desktop those operations can land in adjacent frames, so a line whose
            // Japanese text determines the width visibly passes through intermediate geometry.
            // Derive the physical target from the current native rect and commit all four values
            // in one SetWindowPos call.
            var nativeLeft = currentRect.Left + ToDevicePixels(targetLeft - oldLeft, dpiScaleX);
            var nativeTop = currentRect.Top + ToDevicePixels(targetTop - oldTop, dpiScaleY);
            var nativeWidth = Math.Max(1, ToDevicePixels(targetWidth, dpiScaleX));
            var nativeHeight = Math.Max(1, ToDevicePixels(targetHeight, dpiScaleY));
            appliedNatively = SetWindowPos(handle, IntPtr.Zero,
                nativeLeft, nativeTop, nativeWidth, nativeHeight,
                SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
        }

        // Keep WPF dependency properties synchronized with the native rectangle. When the native
        // call succeeded these assignments target the already-current rectangle and cannot expose
        // the old width-first sequence; the fallback preserves resizing if Win32 rejects the call.
        SetCurrentValue(WidthProperty, targetWidth);
        SetCurrentValue(HeightProperty, targetHeight);
        SetCurrentValue(LeftProperty, targetLeft);
        SetCurrentValue(TopProperty, targetTop);

        if (!appliedNatively)
            AppLog.Info("Layout", "atomic window geometry unavailable; used WPF fallback");
    }

    private void OnResizeCommitTimerTick(object? sender, EventArgs e)
    {
        _resizeCommitTimer.Stop();
        if (!_pointerDown) CommitPendingLyricRender();
    }

    private static double SnapToDevicePixel(double value, double dpiScale) =>
        Math.Round(value * Math.Max(0.1, dpiScale)) / Math.Max(0.1, dpiScale);

    private static int ToDevicePixels(double value, double dpiScale) =>
        (int)Math.Round(value * Math.Max(0.1, dpiScale), MidpointRounding.AwayFromZero);

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
        var opacity = Math.Clamp(AppSettings.Current.KaraokeOpacity, 0.5, 1.0);
        var requestedColor = AppSettings.Current.KaraokeUseBackgroundColor
            ? color
            : Color.FromRgb(45, 60, 204);
        var legible = AlbumColorPalette.NormalizeForWhiteText(
            new ArtworkColor(requestedColor.R, requestedColor.G, requestedColor.B),
            opacity);
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(255 * opacity), legible.Red, legible.Green, legible.Blue));
        brush.Freeze();
        RootBorder.Background = brush;
    }

    // ---- 拖动（NOACTIVATE 窗口不能用 DragMove，手动实现） ----

    private void OnBorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _resizeCommitTimer.Stop();
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
        CommitPendingLyricRender();
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
        AppSettings.Current.Save(notifyListeners: false);
        AppLog.Info("Drag", $"drag completed; left={Left:F1}; top={Top:F1}; " +
                            $"centerX={AppSettings.Current.KaraokeCenterX:F1}");
        e.Handled = true;
    }

    private void CancelPointerGesture()
    {
        _pointerDown = false;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        CommitPendingLyricRender();
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
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int width, int height, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum VerticalResizeAnchor
    {
        Top,
        Center,
        Bottom
    }

    private readonly record struct LyricFontSizes(
        double Primary,
        double Romanized,
        double Translated,
        double Placeholder,
        double PrimaryLineHeight,
        double RomanizedLineHeight,
        double TranslatedLineHeight);

    private readonly record struct LyricRenderSnapshot(
        LyricFontSizes FontSizes,
        string? Primary,
        string? Romanized,
        string? Translated);

    private readonly record struct LyricCardLayout(
        double Width,
        double Height,
        double ContentWidth,
        double CornerRadius);
}
