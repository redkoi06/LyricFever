using System.Windows;
using LyricFever.Windows.App.Services;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// 设置窗口（对应 macOS 版 OnboardingWindow 的 5 Tab 结构，按 Windows 范围精简为 4 Tab）。
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = AppSettings.Current;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.Save();
        Close();
    }
}
