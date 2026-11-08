using System.Windows;

namespace AdbToolkit.Services;

/// <summary>
/// 主题切换：替换 Application.Resources 中的 Colors 字典。
/// 由于 XAML 中使用 StaticResource 引用画刷，运行时切换对已创建的界面不生效，
/// 因此主题在应用启动时应用，修改后需重启（设置页会提示）。
/// </summary>
public static class ThemeManager
{
    public const string Light = "light";
    public const string Dark = "dark";

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
}
