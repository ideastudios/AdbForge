using System.Windows;
using System.Windows.Controls;
using AdbToolkit.ViewModels;

namespace AdbToolkit.Views;

public partial class ScreenInputView : UserControl
{
    public ScreenInputView()
    {
        InitializeComponent();
    }

    private void View_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        => (DataContext as ScreenInputViewModel)?.Shutdown();

    // ---------- 键盘透传窗口 ----------

    private Views.KeyboardForwardWindow? _kbWindow;

    private void OpenKeyboardWindow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_kbWindow is { IsLoaded: true })
        {
            _kbWindow.Activate();
            return;
        }
        _kbWindow = new Views.KeyboardForwardWindow
        {
            Owner = Application.Current.MainWindow,
        };
        _kbWindow.Show();
    }
}
