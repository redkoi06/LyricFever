using System.IO;
using LyricFever.Core.Interfaces;

namespace LyricFever.Windows.App.Services.Translation;

/// <summary>
/// 模型部署：安装包携带 models/en-zh、models/ja-zh（CT2 INT8），
/// 首次启用翻译时从 exe 旁复制到 %APPDATA%\LyricFever\models（避免占用 Program Files 写入权限）。
/// </summary>
public static class ModelInstallService
{
    /// <summary>应用目录内随包分发的模型根目录。</summary>
    public static string BundledModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>确保指定语言模型已部署。返回模型是否可用。</summary>
    public static bool EnsureDeployed(LyricLanguage lang)
    {
        var target = CTranslate2TranslationProvider.ModelDirFor(lang);
        if (Directory.Exists(target)) return true;

        var source = Path.Combine(BundledModelsDir, lang == LyricLanguage.Japanese ? "ja-zh" : "en-zh");
        if (!Directory.Exists(source)) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            CopyDirectory(source, target);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Models] deploy failed: {ex.Message}");
            return false;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        }
    }
}
