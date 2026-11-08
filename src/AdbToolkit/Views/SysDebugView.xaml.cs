using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class SysDebugView : UserControl
{
    public SysDebugView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is SysDebugViewModel vm) await vm.LoadOnNavigateAsync();
        };
    }

    /// <summary>切换到「属性 getprop」标签时自动重新拉取全部属性。</summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 只响应 TabControl 自身的选择变化，忽略内部 ListBox 冒泡上来的事件
        if (e.OriginalSource is not TabControl) return;
        if (DataContext is SysDebugViewModel vm && PropsTabItem.IsSelected)
            vm.RefreshPropsCommand.Execute(null);
    }

    /// <summary>Enter 采纳第一条提示；Esc 关闭提示。</summary>
    private void PropSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SysDebugViewModel vm) return;

        if (e.Key == Key.Enter && vm.PropSuggestionsOpen && vm.PropSuggestions.Count > 0)
        {
            vm.PickPropSuggestion(vm.PropSuggestions[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.PropSuggestionsOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>点击提示项：按该属性名精确过滤。</summary>
    private void PropSuggestions_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SysDebugViewModel vm) return;
        if (PropSuggestionsList.SelectedItem is string key)
        {
            vm.PickPropSuggestion(key);
            PropSuggestionsList.SelectedIndex = -1;
        }
    }

    private void Terminal_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SysDebugViewModel vm)
        {
            vm.RunTerminalCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---------- 属性列表右键 ----------

    private SysDebugViewModel? Vm => DataContext as SysDebugViewModel;

    private static ListViewItem? FindListViewItem(DependencyObject? source)
    {
        while (source != null && source is not ListViewItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        return source as ListViewItem;
    }

    /// <summary>右键时先选中光标下的行（否则点击行不会改变选中项）。</summary>
    private void PropsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindListViewItem(e.OriginalSource as DependencyObject) is ListViewItem item)
            item.IsSelected = true;
    }

    /// <summary>没选中任何属性时不弹右键菜单。</summary>
    private void PropsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (PropsList.SelectedItem == null) e.Handled = true;
    }

    private void EditProp_Click(object sender, RoutedEventArgs e)
        => _ = Vm?.EditSelectedPropAsync();

    private void CopyPropKey_Click(object sender, RoutedEventArgs e)
        => Vm?.CopyProp(copyKey: true);

    private void CopyPropValue_Click(object sender, RoutedEventArgs e)
        => Vm?.CopyProp(copyKey: false);
}
