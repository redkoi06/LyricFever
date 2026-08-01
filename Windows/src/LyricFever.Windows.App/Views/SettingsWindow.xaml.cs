using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.ViewModels;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// 设置窗口（对应 macOS 版 OnboardingWindow 的 5 Tab 结构，按 Windows 范围精简为 4 Tab）。
/// 每个可见控件都接线：保存时应用运行时行为（播放器选择、LaunchAtStartup → 注册表）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = AppSettings.Current;
        _viewModel = App.CurrentApp.MainViewModel
            ?? throw new InvalidOperationException("MainViewModel 未初始化");
        UpdateSpotifyStatus(_viewModel.IsSpotifyLoggedIn);
        UpdateMediaSessionStatus();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSpotifyLoggedIn))
            Dispatcher.InvokeAsync(() => UpdateSpotifyStatus(_viewModel.IsSpotifyLoggedIn));
        else if (e.PropertyName == nameof(MainViewModel.HasMediaSession))
            Dispatcher.InvokeAsync(UpdateMediaSessionStatus);
    }

    private void UpdateMediaSessionStatus()
    {
        var player = AppSettings.Current.PreferredPlayer switch
        {
            "Spotify" => "Spotify",
            "Any" => "系统当前播放器",
            _ => "Apple Music"
        };
        MediaSessionStatusText.Text = _viewModel.HasMediaSession
            ? $"播放器状态：已连接 {player}"
            : $"播放器状态：未检测到 {player}";
    }

    private void UpdateSpotifyStatus(bool loggedIn)
    {
        SpotifyStatusText.Text = loggedIn ? "Spotify 登录状态：已登录" : "Spotify 登录状态：未登录";
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        settings.Save();

        // 应用运行时设置（播放器选择立即生效，无需重启）
        var app = App.CurrentApp;
        if (app.Watcher != null)
        {
            app.Watcher.PreferredPlayer = App.ParsePlayerPreference(settings.PreferredPlayer);
            app.Watcher.ApplySessionFilter();
        }
        ApplyLaunchAtStartup(settings.LaunchAtStartup);

        Close();
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
}
