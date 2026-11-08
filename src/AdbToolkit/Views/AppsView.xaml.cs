using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class AppsView : UserControl
{
    public AppsView()
    {
        InitializeComponent();
    }

    private AppsViewModel? Vm => DataContext as AppsViewModel;

    private static ListViewItem? FindListViewItem(DependencyObject? source)
    {
        while (source != null && source is not ListViewItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        return source as ListViewItem;
    }

    /// <summary>右键时先选中光标下的行，菜单操作即作用于该应用。</summary>
    private void AppsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindListViewItem(e.OriginalSource as DependencyObject) is ListViewItem item)
            item.IsSelected = true;
    }

    /// <summary>没选中任何应用时不弹右键菜单。</summary>
    private void AppsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (AppsList.SelectedItem == null) e.Handled = true;
    }

    private async void ShowDetail_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ShowAppDetailAsync();
    }

    private async void GetLabel_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.GetLabelForSelectedAsync();
    }

    private async void ExtractApk_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.ExtractApkAsync();
    }

    private void CopyLabel_Click(object sender, RoutedEventArgs e)
        => Vm?.CopyAppInfo(copyPath: false, copyLabel: true);

    private void CopyPkg_Click(object sender, RoutedEventArgs e)
        => Vm?.CopyAppInfo(copyPath: false);

    private void CopyPath_Click(object sender, RoutedEventArgs e)
        => Vm?.CopyAppInfo(copyPath: true);

    private async void OpenSystemDetail_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.OpenSystemDetailAsync();
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedApp != null)
            await Vm.UninstallCommand.ExecuteAsync(Vm.SelectedApp);
    }

    // ---------- 拖拽批量安装 ----------

    private static List<string> CollectApks(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return new List<string>();
        var drops = (string[])e.Data.GetData(DataFormats.FileDrop);
        return drops
            .SelectMany(f => Directory.Exists(f)
                ? Directory.GetFiles(f, "*.apk", SearchOption.TopDirectoryOnly)
                : new[] { f })
            .Where(f => f.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        var hasApk = CollectApks(e).Count > 0;
        e.Effects = hasApk ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Page_Drop(object sender, DragEventArgs e)
    {
        var apks = CollectApks(e);
        if (apks.Count > 0 && Vm != null)
            _ = Vm.InstallFilesFromDropAsync(apks);
    }
}
