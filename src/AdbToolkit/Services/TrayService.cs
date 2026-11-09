using System.Drawing;
using System.Windows;
using AdbToolkit.Resources;

namespace AdbToolkit.Services;

/// <summary>
/// 系统托盘图标：启用"关闭窗口时隐藏到托盘"后，主窗口关闭时隐藏到托盘并常驻图标。
/// 使用 WinForms NotifyIcon（WindowsDesktop 共享框架自带，不引入 NuGet 依赖）。
/// </summary>
public static class TrayService
{
    private static System.Windows.Forms.NotifyIcon? _icon;
    private static bool _tipShown;

    /// <summary>确保托盘图标已创建并显示（已存在时不重复创建、不重复弹气泡）。</summary>
    public static void Ensure(Window window)
    {
        if (_icon != null) return;

        var icon = new System.Windows.Forms.NotifyIcon
        {
            Text = Strings.App_Title,
            Visible = true,
        };
        icon.Icon = LoadAppIcon();
        icon.DoubleClick += (_, _) => ShowWindow(window);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Strings.Tray_ShowMainWindow, null, (_, _) => ShowWindow(window));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Exit, null, (_, _) =>
        {
            if (window is MainWindow main) main.ExitFromTray();
        });
        icon.ContextMenuStrip = menu;

        if (!_tipShown)
        {
            _tipShown = true;
            icon.BalloonTipTitle = Strings.Tray_HiddenBalloonTitle;
            icon.BalloonTipText = Strings.Tray_HiddenBalloonText;
            icon.ShowBalloonTip(1500);
        }

        _icon = icon;
    }

    /// <summary>移除并释放托盘图标（真实退出时调用）。</summary>
    public static void Dispose()
    {
        if (_icon == null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static Icon LoadAppIcon()
    {
        // 优先用打包进程序集的 app.ico，失败再从 exe 提取
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/assets/app.ico"))?.Stream;
            if (stream != null) return new Icon(stream);
        }
        catch { /* 资源不可用时回退 */ }
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null) return Icon.ExtractAssociatedIcon(exe)!;
        }
        catch { /* 最终回退系统图标 */ }
        return SystemIcons.Application;
    }
}
