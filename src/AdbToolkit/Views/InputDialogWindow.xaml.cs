using System.Windows;
using System.Windows.Input;

namespace AdbToolkit.Views;

/// <summary>通用单行输入对话框（如"新建文件夹"输入名称）。</summary>
public partial class InputDialogWindow : Window
{
    public string DialogTitle { get; }
    public string Prompt { get; }
    public string Value { get; set; }

    private InputDialogWindow(string title, string prompt, string initial)
    {
        DialogTitle = title;
        Prompt = prompt;
        Value = initial;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    /// <summary>显示输入框，确定返回输入内容，取消返回 null。</summary>
    public static string? Show(string title, string prompt, string initial)
    {
        var win = new InputDialogWindow(title, prompt, initial)
        {
            Owner = Application.Current.MainWindow,
        };
        return win.ShowDialog() == true ? win.Value : null;
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
