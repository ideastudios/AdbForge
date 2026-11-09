using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AdbToolkit.Services;

/// <summary>
/// 主题切换：替换 Application.Resources 中的 Colors 字典。
/// 控件样式统一使用 DynamicResource 引用调色板，主题在应用启动时应用，修改后需重启（设置页会提示）。
/// </summary>
public static class ThemeManager
{
    public const string Light = "light";
    public const string Dark = "dark";

    private static bool IsDark => AppSession.Settings.Theme.Equals(Dark, StringComparison.OrdinalIgnoreCase);

    public static void ApplyFromSettings() => Apply(AppSession.Settings.Theme);

    public static void Apply(string theme)
    {
        var app = Application.Current;
        if (app == null) return;

        var source = new Uri(
            theme.Equals(Dark, StringComparison.OrdinalIgnoreCase)
                ? "Themes/Colors.Dark.xaml"
                : "Themes/Colors.Light.xaml",
            UriKind.Relative);

        var dicts = app.Resources.MergedDictionaries;
        for (var i = 0; i < dicts.Count; i++)
        {
            if (dicts[i].Source?.OriginalString.Contains("Colors.") == true)
            {
                dicts[i] = new ResourceDictionary { Source = source };
                return;
            }
        }
        dicts.Insert(0, new ResourceDictionary { Source = source });
    }

    /// <summary>按当前主题设置窗口标题栏为深色/浅色（Win10 1809+，失败时静默忽略）。</summary>
    public static void ApplyWindowTheme(Window? window)
    {
        try
        {
            if (window == null) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
            // DWM 不可用等场景忽略
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DwmUseImmersiveDarkMode = 20;
}
