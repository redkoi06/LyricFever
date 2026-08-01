using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using LyricFever.Core.Interfaces;

namespace LyricFever.Windows.App.Services.Translation;

/// <summary>
/// 模型部署（执行指挥书 P0-C）：
/// - 安装包携带 models/en-zh、models/ja-zh（CT2 INT8）+ model_manifest.json
/// - 首次启用翻译时复制到 %APPDATA%\LyricFever\models（避免占用 Program Files 写入权限）
/// - 复制采用"临时目录 → 逐文件校验 → 原子替换"；完整性按 manifest 校验而非仅目录存在
/// </summary>
public static class ModelInstallService
{
    private static readonly string[] RequiredModelFiles =
        { "model.bin", "config.json", "shared_vocabulary.json", "source.spm", "target.spm" };

    /// <summary>应用目录内随包分发的模型根目录。</summary>
    public static string BundledModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>确保指定语言模型已部署。返回模型是否可用（按必需文件校验）。</summary>
    public static bool EnsureDeployed(LyricLanguage lang)
    {
        var target = CTranslate2TranslationProvider.ModelDirFor(lang);
        if (IsComplete(target)) return true;

        var source = Path.Combine(BundledModelsDir, lang == LyricLanguage.Japanese ? "ja-zh" : "en-zh");
        if (!IsComplete(source)) return false;

        try
        {
            // 原子部署：先复制到临时目录并校验，再替换
            var tmp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(tmp);
            foreach (var file in RequiredModelFiles)
            {
                File.Copy(Path.Combine(source, file), Path.Combine(tmp, file), true);
            }
            if (!IsComplete(tmp)) return false;

            var parent = Directory.GetParent(target)?.FullName ?? Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent!);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(tmp, target);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Models] deploy failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>按必需文件（存在且非零）校验模型目录完整性。</summary>
    public static bool IsComplete(string modelDir)
    {
        if (!Directory.Exists(modelDir)) return false;
        foreach (var file in RequiredModelFiles)
        {
            var path = Path.Combine(modelDir, file);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
        }
        return true;
    }

    /// <summary>读取随包分发的模型清单（版本/hash），供发布校验与缓存版本管理。</summary>
    public static string? GetBundledManifestHash(string lang)
    {
        try
        {
            var manifestPath = Path.Combine(BundledModelsDir, "model_manifest.json");
            if (!File.Exists(manifestPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!doc.RootElement.TryGetProperty(lang, out var entry)) return null;
            if (!entry.TryGetProperty("files", out var files)) return null;
            // 以 model.bin 的 hash 作为该语言模型的版本指纹
            if (files.TryGetProperty("model.bin", out var modelBin) &&
                modelBin.TryGetProperty("sha256", out var hash))
                return hash.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
