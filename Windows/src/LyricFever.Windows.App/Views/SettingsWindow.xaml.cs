using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.ViewModels;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// Apple Music 单用途设置窗口。所有调整立即作用于字幕，磁盘写入使用短防抖合并。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly LiveSettings _liveSettings;
    private readonly DispatcherTimer _saveTimer;
    private bool _hasPendingSave;

    public SettingsWindow()
    {
        InitializeComponent();
        _viewModel = App.CurrentApp.MainViewModel
            ?? throw new InvalidOperationException("MainViewModel 未初始化");
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += OnSaveTimerTick;
        _liveSettings = new LiveSettings(AppSettings.Current, OnLiveSettingChanged);
        DataContext = _liveSettings;
        DataPathText.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFever");
        UpdateMediaSessionStatus();
        Loaded += OnSettingsLoaded;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            Loaded -= OnSettingsLoaded;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTimerTick;
            PersistSettings();
        };
    }

    private async void OnSettingsLoaded(object sender, RoutedEventArgs e)
    {
        if (!App.CurrentApp.IsVisualInspectionMode) return;

        for (var index = 0; index < SettingsTabs.Items.Count; index++)
        {
            SettingsTabs.SelectedIndex = index;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            SaveVisualSnapshot(index);
        }
        SettingsTabs.SelectedIndex = 0;
    }

    private void SaveVisualSnapshot(int index)
    {
        if (SettingsRoot.ActualWidth <= 0 || SettingsRoot.ActualHeight <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(SettingsRoot);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(SettingsRoot.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(SettingsRoot.ActualHeight * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(SettingsRoot);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Path.GetTempPath(), $"LyricFever-settings-{index}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        AppLog.Info("Visual", $"settings snapshot={path}");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasMediaSession) or
            nameof(MainViewModel.IsPlaying) or
            nameof(MainViewModel.IsFetching) or
            nameof(MainViewModel.CurrentTitle) or
            nameof(MainViewModel.CurrentArtist))
            Dispatcher.InvokeAsync(UpdateMediaSessionStatus);
    }

    private void UpdateMediaSessionStatus()
    {
        var connected = _viewModel.HasMediaSession;
        var accent = (Brush)FindResource("AccentBrush");
        var accentSoft = (Brush)FindResource("AccentSoftBrush");
        var muted = (Brush)FindResource("SurfaceMutedBrush");
        var secondary = (Brush)FindResource("TextSecondaryBrush");
        var tertiary = (Brush)FindResource("TextTertiaryBrush");

        MediaStatusDot.Fill = connected ? accent : tertiary;
        ConnectionBadge.Background = connected ? accentSoft : muted;
        ConnectionBadgeText.Foreground = connected ? accent : secondary;

        if (!connected)
        {
            MediaSessionStatusText.Text = "等待 Apple Music";
            NowPlayingText.Text = "开始播放音乐后，字幕会自动出现";
            ConnectionBadgeText.Text = "等待中";
            return;
        }

        MediaSessionStatusText.Text = _viewModel.IsFetching
            ? "正在获取网络歌词"
            : "Apple Music 已连接";
        ConnectionBadgeText.Text = _viewModel.IsPlaying ? "同步中" : "已连接";
        NowPlayingText.Text = string.IsNullOrWhiteSpace(_viewModel.CurrentTitle)
            ? "已连接媒体会话，正在等待曲目信息"
            : string.IsNullOrWhiteSpace(_viewModel.CurrentArtist)
                ? _viewModel.CurrentTitle
                : $"{_viewModel.CurrentTitle}  ·  {_viewModel.CurrentArtist}";
    }

    private void OnLiveSettingChanged(string propertyName)
    {
        var settings = AppSettings.Current;
        settings.TranslateEnabled = _liveSettings.TranslateEnabled;
        settings.RomanizationEnabled = _liveSettings.RomanizationEnabled;
        settings.KaraokeFontSize = _liveSettings.KaraokeFontSize;
        settings.KaraokeOpacity = _liveSettings.KaraokeOpacity;
        settings.KaraokeUseBackgroundColor = _liveSettings.KaraokeUseBackgroundColor;
        settings.LyricOffsetMs = _liveSettings.LyricOffsetMs;
        settings.LaunchAtStartup = _liveSettings.LaunchAtStartup;

        // 先更新正在显示的字幕；磁盘写入稍后合并，避免滑块拖动造成连续 I/O。
        settings.NotifyChanged(propertyName);
        _hasPendingSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();

        if (propertyName == nameof(LiveSettings.LaunchAtStartup))
            ApplyLaunchAtStartup(settings.LaunchAtStartup);

        if ((propertyName == nameof(LiveSettings.TranslateEnabled) && settings.TranslateEnabled) ||
            (propertyName == nameof(LiveSettings.RomanizationEnabled) && settings.RomanizationEnabled))
            _viewModel.RefreshLyrics();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        PersistSettings();
    }

    private void PersistSettings()
    {
        if (!_hasPendingSave) return;
        _hasPendingSave = false;
        AppSettings.Current.Save(notifyListeners: false);
    }

    /// <summary>开机自启：HKCU Run 键（写入/删除当前 exe 路径）。</summary>
    private static void ApplyLaunchAtStartup(bool enable)
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var runKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKeyPath);
            if (runKey == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) runKey.SetValue("LyricFever", $"\"{exe}\"");
            }
            else
            {
                runKey.DeleteValue("LyricFever", false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LyricFever][Settings] launch at startup failed: {ex.Message}");
        }
    }

    private sealed class LiveSettings : INotifyPropertyChanged
    {
        private readonly Action<string> _onChanged;
        private bool _translateEnabled;
        private bool _romanizationEnabled;
        private double _karaokeFontSize;
        private double _karaokeOpacity;
        private bool _karaokeUseBackgroundColor;
        private int _lyricOffsetMs;
        private bool _launchAtStartup;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool TranslateEnabled
        {
            get => _translateEnabled;
            set => SetField(ref _translateEnabled, value);
        }

        public bool RomanizationEnabled
        {
            get => _romanizationEnabled;
            set => SetField(ref _romanizationEnabled, value);
        }

        public double KaraokeFontSize
        {
            get => _karaokeFontSize;
            set => SetField(ref _karaokeFontSize, Math.Clamp(value, 12, 48));
        }

        public double KaraokeOpacity
        {
            get => _karaokeOpacity;
            set => SetField(ref _karaokeOpacity, Math.Clamp(value, 0.5, 1));
        }

        public bool KaraokeUseBackgroundColor
        {
            get => _karaokeUseBackgroundColor;
            set => SetField(ref _karaokeUseBackgroundColor, value);
        }

        public int LyricOffsetMs
        {
            get => _lyricOffsetMs;
            set => SetField(ref _lyricOffsetMs, Math.Clamp(value, -2000, 2000));
        }

        public bool LaunchAtStartup
        {
            get => _launchAtStartup;
            set => SetField(ref _launchAtStartup, value);
        }

        public LiveSettings(AppSettings settings, Action<string> onChanged)
        {
            _onChanged = onChanged;
            _translateEnabled = settings.TranslateEnabled;
            _romanizationEnabled = settings.RomanizationEnabled;
            _karaokeFontSize = settings.KaraokeFontSize;
            _karaokeOpacity = settings.KaraokeOpacity;
            _karaokeUseBackgroundColor = settings.KaraokeUseBackgroundColor;
            _lyricOffsetMs = settings.LyricOffsetMs;
            _launchAtStartup = settings.LaunchAtStartup;
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            _onChanged(propertyName);
        }
    }
}
