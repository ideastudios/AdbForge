using System.IO;

namespace AdbToolkit.Services;

/// <summary>
/// 本地文件日志：%APPDATA%\AdbToolkit\logs\app_YYYYMMDD.log。
/// 任何写入失败都静默忽略，日志永不影响主功能。
/// </summary>
public static class LogService
{
    private static readonly object Sync = new();

    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdbToolkit", "logs");

    public static void Info(string msg) => Write("INFO", msg, null);

    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    private static void Write(string level, string msg, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"app_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}" +
                    (ex == null ? "" : $"\n{ex}") + Environment.NewLine);
            }
        }
        catch { /* 日志写入失败不影响功能 */ }
    }
}
