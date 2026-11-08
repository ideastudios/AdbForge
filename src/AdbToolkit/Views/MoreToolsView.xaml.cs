using System.Windows;
using System.Windows.Controls;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class MoreToolsView : UserControl
{
    public MoreToolsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is MoreToolsViewModel vm) await vm.LoadOnNavigateAsync();
        };
    }

    private void PickZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 OTA zip 包",
            Filter = "OTA 包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true && DataContext is MoreToolsViewModel vm)
            vm.SideloadZip = dialog.FileName;
    }
}
