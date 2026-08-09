using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.ViewModels;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// 设置窗口（对应 macOS 版 OnboardingWindow 的 5 Tab 结构，按 Windows 范围精简为 4 Tab）。
/// 每个可见控件都接线；播放器固定为 Apple Music，保存时应用开机启动设置。
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
        UpdateMediaSessionStatus();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasMediaSession))
            Dispatcher.InvokeAsync(UpdateMediaSessionStatus);
    }

    private void UpdateMediaSessionStatus()
    {
        MediaSessionStatusText.Text = _viewModel.HasMediaSession
            ? "播放器状态：已连接 Apple Music"
            : "播放器状态：未检测到 Apple Music";
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        settings.Save();

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
