using System.IO;
using System.Runtime.InteropServices;
using LyricFever.Core.Interfaces;

namespace LyricFever.Windows.App.Services.Translation;

/// <summary>
/// CTranslate2 翻译实现（对应用户定案：C++ DLL + P/Invoke，OPUS-MT en-zh/ja-zh INT8）。
/// 模型目录约定：%APPDATA%\LyricFever\models\en-zh / ja-zh（CT2 格式目录）。
/// 所有原生调用在后台线程执行（Task.Run），不阻塞 WPF UI 线程。
/// </summary>
public sealed class CTranslate2TranslationProvider : ITranslationProvider
{
    private const string DllName = "LyricFeverTranslation.dll";

    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LyricFever", "models");

    private LyricLanguage? _loadedLanguage;
    private readonly object _lock = new();

    public bool IsLoaded
    {
        get { lock (_lock) return _loadedLanguage != null; }
    }

    /// <summary>模型可用性（供设置页显示下载状态）。</summary>
    public bool IsModelDownloaded(LyricLanguage lang) => Directory.Exists(ModelDirFor(lang));

    public static string ModelDirFor(LyricLanguage lang) =>
        Path.Combine(ModelsDir, lang == LyricLanguage.Japanese ? "ja-zh" : "en-zh");

    public Task LoadAsync(LyricLanguage sourceLanguage, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_loadedLanguage == sourceLanguage) return Task.CompletedTask;
        }

        // 首次使用从安装包复制模型到应用数据目录
        if (!ModelInstallService.EnsureDeployed(sourceLanguage))
            throw new FileNotFoundException($"翻译模型不可用：{ModelDirFor(sourceLanguage)}");

        var modelDir = ModelDirFor(sourceLanguage);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_loadedLanguage == sourceLanguage) return;
                UnloadLocked();
                var rc = lf_load_model(modelDir, 1, 2); // inter_threads=1, intra_threads=2（用户定案）
                if (rc != 0) throw new InvalidOperationException($"模型加载失败（错误码 {rc}）");
                _loadedLanguage = sourceLanguage;
            }
        }, cancellationToken);
    }

    public Task UnloadAsync()
    {
        return Task.Run(() => Unload());
    }

    public void Unload()
    {
        lock (_lock)
        {
            UnloadLocked();
        }
    }

    private void UnloadLocked()
    {
        if (_loadedLanguage != null)
        {
            lf_unload_model();
            _loadedLanguage = null;
        }
    }

    public Task<IReadOnlyList<TranslationResponse>> TranslateAsync(
        IReadOnlyList<TranslationRequest> requests, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
            return Task.FromResult<IReadOnlyList<TranslationResponse>>(Array.Empty<TranslationResponse>());

        // 模型已由 LoadAsync 按源语言加载；按目标语言映射 CT2 语言码
        var srcCode = SourceCodeFor(requests[0].SourceLanguage);
        var tgtCode = TargetCodeFor(targetLanguage);
        var lines = requests.Select(r => r.Text).ToArray();

        // 原生调用在后台线程执行，不阻塞 UI
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int rc;
            IntPtr outLines;
            int outCount;
            lock (_lock)
            {
                rc = lf_translate_batch(lines, lines.Length, srcCode, tgtCode,
                    out outLines, out outCount);
            }
            if (rc != 0) throw new InvalidOperationException($"翻译失败（错误码 {rc}）");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = new List<TranslationResponse>(outCount);
                for (var i = 0; i < outCount && i < requests.Count; i++)
                {
                    var ptr = Marshal.ReadIntPtr(outLines, i * IntPtr.Size);
                    var text = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
                    results.Add(new TranslationResponse(requests[i].LineId, text, true));
                }
                return (IReadOnlyList<TranslationResponse>)results;
            }
            finally
            {
                lf_free_lines(outLines, outCount);
            }
        }, cancellationToken);
    }

    /// <summary>源语言 → CT2/Marian 语言码（en-zh 模型用 en，ja-zh 模型用 jp —— Marian 的 ja 码是 jp）。</summary>
    private static string SourceCodeFor(LyricLanguage lang) =>
        lang == LyricLanguage.Japanese ? "jp" : "en";

    private static string TargetCodeFor(string targetLanguage) =>
        targetLanguage.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : targetLanguage;

    // ---- P/Invoke ----

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int lf_load_model(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
        int interThreads, int intraThreads);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int lf_translate_batch(
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPUTF8Str)] string[] lines,
        int count,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceLang,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string targetLang,
        out IntPtr outLines, out int outCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void lf_free_lines(IntPtr lines, int count);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void lf_unload_model();
}
