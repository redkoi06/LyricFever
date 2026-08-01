using System.Windows;
using LyricFever.Windows.App.Services;

namespace LyricFever.Windows.App.Views;

/// <summary>
/// Spotify 登录窗口（对应 macOS WebLoginView + ApiView 的 WKWebView 登录流程）：
/// 打开 accounts.spotify.com 登录，跳转回 open.spotify.com 后抓取 sp_dc cookie 并保存。
/// </summary>
public partial class SpotifyLoginWindow : Window
{
    private bool _cookieCaptured;

    public SpotifyLoginWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // WebView2 环境初始化（首次较慢）
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                userDataFolder: System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LyricFever", "WebView2"));
            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.Navigate("https://accounts.spotify.com/en/login?continue=https%3A%2F%2Fopen.spotify.com%2F");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法启动登录页面：{ex.Message}\n\n可改用“手动粘贴 sp_dc Cookie”方式。", "Lyric Fever");
            Close();
        }
    }

    private async void OnNavigationStarting(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        // 登录成功跳回 open.spotify.com 时抓 cookie（对应 macOS checkIfLoggedIn 的 WKWebsiteDataStore 流程）
        if (_cookieCaptured || !args.Uri.StartsWith("https://open.spotify.com", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(args.Uri);
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "sp_dc" && !string.IsNullOrEmpty(cookie.Value))
                {
                    CredentialStore.Set("spotify.sp_dc", cookie.Value);
                    _cookieCaptured = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show("Spotify 登录成功！", "Lyric Fever");
                        Close();
                    });
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Login] cookie capture failed: {ex.Message}");
        }
    }
}
