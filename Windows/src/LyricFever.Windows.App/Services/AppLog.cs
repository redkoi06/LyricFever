using System.IO;

namespace LyricFever.Windows.App.Services;

/// <summary>不含凭据的轻量运行日志，便于诊断托盘应用启动、SMTC 和歌词链路。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LyricFever", "logs");
    public static readonly string CurrentPath = Path.Combine(LogDirectory, "app.log");

    public static void Initialize()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(LogDirectory);
            if (File.Exists(CurrentPath) && new FileInfo(CurrentPath).Length > 2 * 1024 * 1024)
                File.Move(CurrentPath, Path.Combine(LogDirectory, "app.previous.log"), true);
        }
        Info("App", "process started");
    }

    public static void Info(string area, string message) => Write("INFO", area, message);
    public static void Error(string area, Exception exception) =>
        Write("ERROR", area, exception.ToString());

    private static void Write(string level, string area, string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [{area}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentPath, line);
            }
        }
        catch
        {
            // 日志失败不能影响歌词主链路。
        }
    }
}
