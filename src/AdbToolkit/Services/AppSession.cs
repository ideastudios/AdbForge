namespace AdbToolkit.Services;

/// <summary>
/// 全局会话：设置 + AdbService 单例。应用启动时初始化一次。
/// </summary>
public static class AppSession
{
    public static AppSettings Settings { get; } = AppSettings.Load();

    public static AdbService Adb { get; } = new()
    {
        AdbPath = string.IsNullOrWhiteSpace(Settings.AdbPath)
            ? AdbService.DetectAdbPath()
            : Settings.AdbPath,
    };

    /// <summary>设置页修改 adb 路径后调用，同时持久化。</summary>
    public static void ApplyAdbPath(string path)
    {
        Adb.AdbPath = path;
        Settings.AdbPath = path;
        Settings.Save();
    }
}
