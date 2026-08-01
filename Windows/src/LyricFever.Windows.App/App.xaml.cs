using System.Windows;
using LyricFever.Windows.App.Services;

namespace LyricFever.Windows.App;

/// <summary>
/// 托盘常驻应用。无默认主窗口：所有交互从系统托盘开始。
/// </summary>
public partial class App : Application
{
    private TrayIconService? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _trayIcon = new TrayIconService();
        _trayIcon.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
