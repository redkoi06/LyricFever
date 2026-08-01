using System.IO;
using System.Text.Json;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 应用设置（JSON 落盘，位于 %APPDATA%\LyricFever\settings.json）。
/// 与 macOS 版 UserDefaults 对应；敏感字段（sp_dc）单独 DPAPI 加密存储。
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LyricFever", "settings.json");

    // ---- 播放器 ----
    public bool UseSpotify { get; set; } = true;

    // ---- 翻译 ----
    /// <summary>翻译开关。默认关闭 —— 关闭时不加载任何翻译模型。</summary>
    public bool TranslateEnabled { get; set; }
    /// <summary>源语言：auto / en / ja。目标语言固定中文。</summary>
    public string SourceLanguage { get; set; } = "auto";

    // ---- 罗马音 ----
    public bool RomanizationEnabled { get; set; } = true;

    // ---- K 歌窗口 ----
    public double KaraokeFontSize { get; set; } = 24;
    public double KaraokeOpacity { get; set; } = 0.9;
    public bool KaraokeUseBackgroundColor { get; set; } = true;
    /// <summary>歌词进度偏移（毫秒，正数提前显示）。</summary>
    public int LyricOffsetMs { get; set; }

    // ---- 通用 ----
    public bool LaunchAtStartup { get; set; }

    // ---- 翻译产物缓存版本（模型升级时 +1 使旧缓存失效） ----
    public int TranslationModelVersion { get; set; } = 1;
    public int RomanizationVersion { get; set; } = 1;

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Settings] save failed: {ex.Message}");
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Settings] load failed: {ex.Message}");
        }
        return new AppSettings();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
