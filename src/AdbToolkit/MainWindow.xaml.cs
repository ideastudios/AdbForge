using System.Windows;
using AdbToolkit.ViewModels;

namespace AdbToolkit;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
