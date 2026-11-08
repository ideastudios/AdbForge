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
            if (DataContext is PerfViewModel vm) await vm.LoadOnNavigateAsync();
        };
    }
}
