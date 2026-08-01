using System.Windows;
using LyricFever.Core.Providers;
using LyricFever.Core.Providers.Spotify;
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

    public MainViewModel? MainViewModel { get; private set; }
    public SpotifyLyricProvider SpotifyProvider { get; private set; } = new();
    public LyricsRepository? LyricsRepository { get; private set; }
    public TranslationCache? TranslationCache { get; private set; }

    public static App CurrentApp => (App)Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 数据层
        var db = new SqliteDatabase();
        LyricsRepository = new LyricsRepository(db);
        TranslationCache = new TranslationCache(db);

        // Spotify 凭据恢复
        var cookie = CredentialStore.Get("spotify.sp_dc");
        SpotifyProvider.SpDcCookie = cookie;

        // 服务组装
        var trackMapper = new SpotifyTrackMapper(SpotifyProvider, db);
        var fetchService = new LyricFetchService(LyricsRepository,
            new ILyricProvider[] { SpotifyProvider, new LrclibLyricProvider(), new NetEaseLyricProvider() });
        var watcher = new MediaSessionWatcher();
        var translationPipeline = new TranslationPipelineService(
            new CTranslate2TranslationProvider(),
            new KawazuRomanizationProvider(),
            TranslationCache);

        MainViewModel = new MainViewModel(watcher, trackMapper, fetchService, LyricsRepository, translationPipeline);
        _trayIcon = new TrayIconService(MainViewModel);
        _trayIcon.Initialize();

        await MainViewModel.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
