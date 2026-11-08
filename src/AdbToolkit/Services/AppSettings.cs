using System.IO;
using System.Text.Json;

namespace AdbToolkit.Services;

/// <summary>应用设置，持久化到 %APPDATA%\AdbToolkit\settings.json。</summary>
public class AppSettings
{
    /// <summary>adb.exe 完整路径；为空表示自动探测。</summary>
    public string AdbPath { get; set; } = "";

    /// <summary>设备列表轮询间隔（毫秒）。</summary>
    public int DevicePollMs { get; set; } = 2000;

    /// <summary>Logcat 内存中保留的最大行数。</summary>
    public int LogcatBufferLines { get; set; } = 5000;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdbToolkit");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings != null) return settings;
            }
        }
        catch
        {
            // 配置损坏时退回默认值
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }
}
