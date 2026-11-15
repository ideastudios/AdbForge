using System.Windows.Controls;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class PerfView : UserControl
{
    public PerfView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is PerfViewModel vm)
            {
                vm.ResumeTimers();
                await vm.LoadOnNavigateAsync();
            }
        };
        // 切走页面时停止自动刷新 Timer，避免后台继续跑 adb 命令
        Unloaded += (_, _) => (DataContext as PerfViewModel)?.SuspendTimers();
    }
}
