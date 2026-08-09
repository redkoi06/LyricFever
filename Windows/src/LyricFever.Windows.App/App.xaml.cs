using System.IO;
using System.Windows;
using LyricFever.Core.Providers;
using LyricFever.Core.Storage;
using LyricFever.Windows.App.Services;
using LyricFever.Windows.App.Services.Translation;
using LyricFever.Windows.App.ViewModels;

namespace LyricFever.Windows.App;

/// <summary>
/// 托盘常驻应用。无默认主窗口：所有交互从系统托盘开始。
/// </summary>
public partial class App : Application
{
    private TrayIconService? _trayIcon;
    private MediaSessionWatcher? _watcher;
    private SingleInstanceCoordinator? _singleInstance;

    public MainViewModel? MainViewModel { get; private set; }
    public bool IsVisualInspectionMode { get; private set; }

    public static App CurrentApp => (App)Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        EnsureWindowsDirectoryEnvironment();
        base.OnStartup(e);
        IsVisualInspectionMode = e.Args.Contains("--visual-inspection", StringComparer.OrdinalIgnoreCase);
        AppLog.Initialize();
        AppLog.Info("App", $"visualInspection={IsVisualInspectionMode}, player=AppleMusic");
        DispatcherUnhandledException += (_, args) => AppLog.Error("Dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) AppLog.Error("AppDomain", exception);
        };

        if (!SingleInstanceCoordinator.TryAcquire(out _singleInstance))
        {
            Shutdown(0);
            return;
        }

        RemoveLegacyLocalModels();

        // 数据层
        var db = new SqliteDatabase();
        var lyricsRepository = new LyricsRepository(db);
        var translationCache = new TranslationCache(db);
        var removedTranslations = translationCache.DeleteVersionsOlderThan(
            AppSettings.TranslationCacheVersion);
        if (removedTranslations > 0)
            AppLog.Info("Translate", $"removed {removedTranslations} retired translation cache entries");

        // 服务组装
        var netEaseProvider = new NetEaseLyricProvider();
        var fetchService = new LyricFetchService(lyricsRepository,
            new ILyricProvider[] { new LrclibLyricProvider(), netEaseProvider });
        _watcher = new MediaSessionWatcher();
        var translationPipeline = new TranslationPipelineService(
            new KawazuRomanizationProvider(),
            translationCache,
            netEaseProvider);

        MainViewModel = new MainViewModel(
            _watcher, fetchService, lyricsRepository, translationPipeline, netEaseProvider);
        _trayIcon = new TrayIconService(MainViewModel);
        _trayIcon.Initialize();
        _singleInstance!.StartListening(() => Dispatcher.BeginInvoke(_trayIcon.ShowLyricsWindow));

        await MainViewModel.StartAsync();
        AppLog.Info("App", "startup completed");
    }

    private static void EnsureWindowsDirectoryEnvironment()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir"))) return;
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
            Environment.SetEnvironmentVariable("windir", windowsDirectory);
    }

    private static void RemoveLegacyLocalModels()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LyricFever", "models");
        try
        {
            if (!Directory.Exists(path)) return;
            Directory.Delete(path, recursive: true);
            AppLog.Info("Translate", $"removed retired local model directory: {path}");
        }
        catch (Exception ex)
        {
            // 清理失败不能阻断人工译词和播放器监听；下次启动继续尝试。
            AppLog.Error("Translate-Cleanup", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        MainViewModel?.Dispose();
        _watcher?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
