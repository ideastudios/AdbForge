using System.ComponentModel;
using System.Windows;
using AdbToolkit.Services;
using AdbToolkit.ViewModels;

namespace AdbToolkit;

public partial class MainWindow : Window
{
    /// <summary>托盘菜单"退出"时置 true，OnClosing 不再拦截。</summary>
    private bool _realExit;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += MainWindow_Loaded;
    }

    /// <summary>首次运行时弹出引导窗口（只弹一次）。</summary>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (AppSession.Settings.FirstRunDone) return;
        AppSession.Settings.FirstRunDone = true;
        AppSession.Settings.Save();
        new Views.FirstRunWindow { Owner = this }.ShowDialog();
    }

    /// <summary>
    /// 关闭窗口：若开启"关闭时隐藏到托盘"则拦截关闭、隐藏窗口并常驻托盘；
    /// 否则正常退出并清理托盘图标。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        if (!_realExit && AppSession.Settings.HideToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            TrayService.Ensure(this);
            return;
        }

        TrayService.Dispose();
    }

    /// <summary>托盘菜单"退出"：真正关闭应用。</summary>
    public void ExitFromTray()
    {
        _realExit = true;
        Close();
    }
}
