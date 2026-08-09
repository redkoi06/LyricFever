using System.IO;
using System.Text.Json;

namespace LyricFever.Windows.App.Services;

/// <summary>
/// 应用设置（JSON 落盘，位于 %APPDATA%\LyricFever\settings.json）。
/// JSON 中的未知字段由序列化器忽略；开发阶段不保留退役功能的迁移分支。
/// </summary>
public sealed class AppSettings
{
    public const int TranslationCacheVersion = 4;
    public const int RomanizationCacheVersion = 1;
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LyricFever", "settings.json");

    // ---- 翻译 ----
    /// <summary>显示经过曲目校验的平台人工译词。新安装默认启用。</summary>
    public bool TranslateEnabled { get; set; } = true;

    // ---- 罗马音 ----
    public bool RomanizationEnabled { get; set; } = true;

    // ---- K 歌窗口 ----
    public double KaraokeFontSize { get; set; } = 24;
    public double KaraokeOpacity { get; set; } = 0.82;
    public bool KaraokeUseBackgroundColor { get; set; } = true;
    public double? KaraokeLeft { get; set; }
    public double? KaraokeTop { get; set; }
    /// <summary>悬浮字幕的水平中轴；宽度变化时以此为锚点向左右对称伸缩。</summary>
    public double? KaraokeCenterX { get; set; }
    /// <summary>歌词进度偏移（毫秒，正数提前显示）。</summary>
    public int LyricOffsetMs { get; set; }

    // ---- 通用 ----
    public bool LaunchAtStartup { get; set; }

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public static event Action<string?>? SettingsChanged;

    /// <summary>通知运行中的窗口立即采用当前内存设置。</summary>
    public void NotifyChanged(string? propertyName = null) => SettingsChanged?.Invoke(propertyName);

    /// <summary>
    /// 将当前设置写入磁盘。设置窗口会先即时通知 UI，再以防抖方式静默持久化，
    /// 避免拖动滑块时反复触发布局更新。
    /// </summary>
    public void Save(bool notifyListeners = true)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
            if (notifyListeners) NotifyChanged();
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
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
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
