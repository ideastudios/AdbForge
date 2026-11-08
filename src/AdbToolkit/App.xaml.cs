using System.Windows;
using AdbToolkit.Services;

namespace AdbToolkit;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 主题必须在任何界面渲染前应用
        ThemeManager.ApplyFromSettings();
        base.OnStartup(e);

        LogService.Info($"应用启动 v{UpdateService.CurrentVersion}");

        // UI 线程未处理异常：记录日志 + 友好提示，不直接崩溃
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("UI 线程未处理异常", args.Exception);
            MessageBox.Show(
                $"发生未处理的错误：{args.Exception.Message}\n\n" +
                $"详细日志已保存到：\n{LogService.LogDir}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
        LogService.Info($"应用退出（code {e.ApplicationExitCode}）");
        base.OnExit(e);
    }
}
