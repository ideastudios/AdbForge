using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class LogcatView : UserControl
{
    private DispatcherTimer? _scrollTimer;

    public LogcatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookAutoScroll();
    }

    /// <summary>
    /// 自动滚动去抖：固定周期滚到最后一行一次。
    /// 不能挂在 CollectionChanged 上逐行滚——每行 ScrollIntoView 都强制布局，高频日志会把 UI 线程打爆。
    /// </summary>
    private void HookAutoScroll()
    {
        if (DataContext is not LogcatViewModel vm) return;

        _scrollTimer?.Stop();
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _scrollTimer.Tick += (_, _) =>
        {
            if (!vm.AutoScroll || !vm.IsCapturing) return;
            if (LogsList.Items.Count > 0)
                LogsList.ScrollIntoView(LogsList.Items[^1]);
        };
        _scrollTimer.Start();
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        _scrollTimer?.Stop();
        (DataContext as LogcatViewModel)?.Shutdown();
    }
}
