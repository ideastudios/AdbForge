using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class FilesView : UserControl
{
    public FilesView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is FilesViewModel vm) await vm.LoadOnNavigateAsync();
        };
    }

    private FilesViewModel? Vm => DataContext as FilesViewModel;

    /// <summary>同步树选中项到 ViewModel（TreeView.SelectedItem 为只读，需在此桥接）。</summary>
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is FilesViewModel vm) vm.SelectedNode = e.NewValue as FileTreeNode;
    }

    /// <summary>右键时先选中光标下的节点，菜单操作即作用于该节点。</summary>
    private void Tree_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeViewItem(e.OriginalSource as DependencyObject) is TreeViewItem item)
            item.IsSelected = true;
    }

    /// <summary>树中没有任何节点时不弹右键菜单。</summary>
    private void Tree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm?.RootNodes.Count == 0) e.Handled = true;
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source != null && source is not TreeViewItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        return source as TreeViewItem;
    }

    private async void RootPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is not null)
        {
            await Vm.SetRootCommand.ExecuteAsync();
            e.Handled = true;
        }
    }

    private async void RootSdcard_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.RootPath = "/sdcard";
        await Vm.SetRootCommand.ExecuteAsync();
    }

    private async void RootTmp_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.RootPath = "/data/local/tmp";
        await Vm.SetRootCommand.ExecuteAsync();
    }

    private async void RootRoot_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.RootPath = "/";
        await Vm.SetRootCommand.ExecuteAsync();
    }

    /// <summary>双击文件节点 → 直接拉取到本机。</summary>
    private async void Tree_DoubleClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var node = Vm.SelectedNode;
        if (node is { IsPlaceholder: false, IsExpandable: false })
            await Vm.PullCommand.ExecuteAsync();
    }

    // ---------- 右键菜单 ----------

    private async void MenuPushFile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.PushViaDialogAsync(isFolder: false);
    }

    private async void MenuPushFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.PushViaDialogAsync(isFolder: true);
    }

    private async void MenuPull_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.PullViaDialogAsync();
    }

    private async void MenuCreateDir_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.CreateDirViaDialogAsync();
    }

    private async void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.DeleteCommand.ExecuteAsync();
    }

    private async void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.RefreshNodeCommand.ExecuteAsync();
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
        => Vm?.CopySelectedPath();
}

/// <summary>RelayCommand 的异步便捷扩展（code-behind 里顺序执行用）。</summary>
public static class RelayCommandExtensions
{
    public static async Task ExecuteAsync(this System.Windows.Input.ICommand? command, object? parameter = null)
    {
        if (command != null && command.CanExecute(parameter)) command.Execute(parameter);
        await Task.CompletedTask;
    }
}
