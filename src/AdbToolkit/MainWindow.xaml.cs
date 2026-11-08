using System.Windows;
using AdbToolkit.Services;
using AdbToolkit.ViewModels;

namespace AdbToolkit;

public partial class MainWindow : Window
{
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
}
