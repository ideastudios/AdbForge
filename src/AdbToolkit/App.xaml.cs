using System.Globalization;
using System.Windows;
using AdbToolkit.Resources;
using AdbToolkit.Services;

namespace AdbToolkit;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 语言与主题必须在任何界面渲染前应用
        ApplyLanguageFromSettings();
        ThemeManager.ApplyFromSettings();
        base.OnStartup(e);

        LogService.Info($"应用启动 v{UpdateService.CurrentVersion}");

        // 每个窗口加载时按当前主题设置深色/浅色标题栏
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ThemeManager.ApplyWindowTheme(s as Window)));

        // UI 线程未处理异常：记录日志 + 友好提示，不直接崩溃
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("UI 线程未处理异常", args.Exception);
            MessageBox.Show(
                string.Format(Strings.App_UnhandledErrorFormat, args.Exception.Message, LogService.LogDir),
                Strings.Common_Error, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 后台任务未观察异常：记录并标记已处理
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        // 致命异常（非 UI 线程）：只能记录，进程即将退出
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogService.Error("致命异常（进程即将退出）", args.ExceptionObject as Exception);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayService.Dispose();
        LogService.Info($"应用退出（code {e.ApplicationExitCode}）");
        base.OnExit(e);
    }

    /// <summary>按设置语言切换资源查找文化（仅影响 UI 语言；数字/日期仍跟随系统）。</summary>
    private static void ApplyLanguageFromSettings()
    {
        var lang = AppSession.Settings.Language;
        CultureInfo.CurrentUICulture = lang.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("en-US")
            : new CultureInfo("zh-CN");
    }
}
