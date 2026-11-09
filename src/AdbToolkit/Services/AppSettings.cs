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

    /// <summary>上次 APK 提取目录（提取对话框记住位置）。</summary>
    public string LastExtractDir { get; set; } = "";

    /// <summary>主题："light" / "dark"（启动时应用，修改后需重启）。</summary>
    public string Theme { get; set; } = ThemeManager.Light;

    /// <summary>界面语言："zh" / "en"（启动时应用，修改后需重启）。</summary>
    public string Language { get; set; } = "zh";

    /// <summary>关闭主窗口时是否隐藏到系统托盘（关闭 = 直接退出）。</summary>
    public bool HideToTrayOnClose { get; set; }

    /// <summary>更新服务器地址（托管版本描述 JSON）；为空表示不检查。</summary>
    public string UpdateUrl { get; set; } = "";

    /// <summary>是否已完成首次运行引导。</summary>
    public bool FirstRunDone { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdbToolkit");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                // 兼容 PowerShell Set-Content -Encoding UTF8 写入的 BOM（否则 JSON 解析失败、设置全部回退默认）
                if (json.Length > 0 && json[0] == '\uFEFF') json = json[1..];
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
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
