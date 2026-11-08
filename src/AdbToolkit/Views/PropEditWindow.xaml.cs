using System.Windows;
using System.Windows.Input;

namespace AdbToolkit.Views;

/// <summary>修改系统属性值的对话框（setprop）。</summary>
public partial class PropEditWindow : Window
{
    public string PropKey { get; }

    public string PropValue { get; set; }

    public PropEditWindow(string key, string value)
    {
        PropKey = key;
        PropValue = value;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Confirm()
    {
        DialogResult = true;
        Close();
    }
}
