namespace AdbToolkit.Models;

/// <summary>设备当前前台应用 / 可见窗口信息。</summary>
public class ForegroundInfo
{
    public ForegroundInfo(string package, string component, string source)
    {
        Package = package;
        Component = component;
        Source = source;
        IsSystem = IsSystemPackage(package);
    }

    /// <summary>应用包名，如 com.example.app。</summary>
    public string Package { get; }

    /// <summary>组件名，如 com.example.app/.MainActivity（与 dumpsys 输出一致）。</summary>
    public string Component { get; }

    /// <summary>来源：Activity（栈顶）/ Window（焦点窗口）/ 窗口（可见窗口列表）。</summary>
    public string Source { get; }

    /// <summary>是否系统包（SystemUI/桌面/GMS 等）。</summary>
    public bool IsSystem { get; }

    /// <summary>是否为当前栈顶前台（可见窗口列表中标记用）。</summary>
    public bool IsForeground { get; set; }

    /// <summary>Activity 部分，如 .MainActivity。</summary>
    public string ActivityName
    {
        get
        {
            var i = Component.IndexOf('/');
            return i >= 0 && i < Component.Length - 1 ? Component[(i + 1)..] : Component;
        }
    }

    public static bool IsSystemPackage(string package) =>
        package == "android" ||
        package.StartsWith("com.android.", StringComparison.Ordinal) ||
        package.StartsWith("com.google.android.", StringComparison.Ordinal) ||
        package.Contains("launcher", StringComparison.OrdinalIgnoreCase);
}
