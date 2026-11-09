using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AdbToolkit.Models;
using AdbToolkit.Resources;
using AdbToolkit.Services;

namespace AdbToolkit.Views;

/// <summary>
/// 键盘透传窗口：PC 键盘事件 → adb input。
/// 文本字符走 input text（150ms 批量合并），系统键走 keyevent，
/// Ctrl/Alt 组合走 keycombination（Android 11+）。
/// </summary>
public partial class KeyboardForwardWindow : Window
{
    private const int MaxPendingCommands = 40;   // 防止长按堆积导致命令风暴
    private const int MaxCommandsPerFlush = 6;   // 每个刷新周期最多执行的按键数
    private const int MaxTextChunk = 200;        // 单次 input text 的最大字符数

    private readonly StringBuilder _pendingText = new();
    private readonly Queue<string> _pendingCommands = new();
    private readonly object _sync = new();
    private readonly DispatcherTimer _flushTimer;
    private bool _sending;
    private int _sentCount;

    public KeyboardForwardWindow()
    {
        InitializeComponent();
        // 捕获子控件已处理过的按键事件（如日志列表吃掉的方向键）
        AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(Window_KeyDown), handledEventsToo: true);
        AddHandler(TextInputEvent, new TextCompositionEventHandler(Window_TextInput), handledEventsToo: true);

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _flushTimer.Tick += (_, _) => _ = FlushAsync();
        _flushTimer.Start();
        Closed += (_, _) => _flushTimer.Stop();
    }

    private string? Serial =>
        DeviceStore.Selected?.State == AdbDeviceState.Device ? DeviceStore.SelectedSerial : null;

    private static int? MapKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return (int)key - (int)Key.A + 29;   // KEYCODE_A..Z
        if (key is >= Key.D0 and <= Key.D9) return (int)key - (int)Key.D0 + 7; // KEYCODE_0..9
        return MapSystemKey(key);
    }

    /// <summary>
    /// 仅系统键走 keyevent。字母/数字/空格不在此列——它们必须走 input text 文本通道，
    /// 否则 Shift/Caps Lock 的大小写信息会丢失（keyevent 29 永远输出小写 a）。
    /// </summary>
    private static int? MapSystemKey(Key key) => key switch
    {
        Key.Enter => 66,
        Key.Back => 67,
        Key.Delete => 112,
        Key.Escape => 111,
        Key.Tab => 61,
        Key.Up => 19,
        Key.Down => 20,
        Key.Left => 21,
        Key.Right => 22,
        Key.PageUp => 92,
        Key.PageDown => 93,
        Key.Home => 122,
        Key.End => 123,
        Key.Insert => 124,
        Key.Apps => 82,
        _ => null,
    };

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Alt 组合时 e.Key 是 System，真实键在 SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 纯修饰键不转发
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        if (Serial is null)
        {
            SetStatus(Strings.Kb_SelectDeviceInMain);
            e.Handled = true;
            return;
        }

        // Ctrl/Alt 组合 → keycombination（meta 键码在前）
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            var code = MapKey(key);
            if (code != null)
            {
                var meta = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 113 : 57;
                Enqueue($"input keycombination {meta} {code}");
                e.Handled = true;
                return;
            }
        }

        // 系统键 → keyevent；字母/数字/空格不拦截，交给 TextInput 文本通道（保留大小写）
        if (MapSystemKey(key) is int kc)
        {
            Enqueue($"input keyevent {kc}");
            e.Handled = true;
            return;
        }

        // 其余可打印字符键不标记 Handled，交给 TextInput 走文本通道
    }

    private void Window_TextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        if (Serial is null)
        {
            SetStatus(Strings.Kb_SelectDeviceInMain);
            return;
        }
        foreach (var c in e.Text)
        {
            // 只收 ASCII 可打印字符；中文等交给剪贴板方案
            if (c is >= (char)0x20 and <= (char)0x7E)
            {
                lock (_sync) _pendingText.Append(c);
            }
            else if (c > (char)0x7E)
            {
                SetStatus(Strings.Kb_NonAsciiHint);
            }
        }
    }

    private void Enqueue(string cmd)
    {
        lock (_sync)
        {
            if (_pendingCommands.Count >= MaxPendingCommands) _pendingCommands.Dequeue();
            _pendingCommands.Enqueue(cmd);
        }
    }

    /// <summary>批量刷新：文本合并成 input text，按键逐条执行。单飞防止堆积。</summary>
    private async Task FlushAsync()
    {
        if (_sending) return;
        var serial = Serial;
        if (serial is null) return;

        string text;
        string[] cmds;
        lock (_sync)
        {
            text = _pendingText.ToString();
            _pendingText.Clear();
            var n = Math.Min(_pendingCommands.Count, MaxCommandsPerFlush);
            cmds = new string[n];
            for (var i = 0; i < n; i++) cmds[i] = _pendingCommands.Dequeue();
        }
        if (text.Length == 0 && cmds.Length == 0) return;

        _sending = true;
        try
        {
            for (var offset = 0; offset < text.Length; offset += MaxTextChunk)
            {
                var len = Math.Min(MaxTextChunk, text.Length - offset);
                await SendAsync(serial, $"input text {QuoteText(text.Substring(offset, len))}");
            }
            foreach (var cmd in cmds) await SendAsync(serial, cmd);
        }
        finally
        {
            _sending = false;
        }
    }

    private async Task SendAsync(string serial, string shellCmd)
    {
        try
        {
            var r = await AppSession.Adb.RunShellAsync(shellCmd, serial);
            _sentCount++;
            LogList.Items.Insert(0, $"[{_sentCount}] {shellCmd}");
            if (LogList.Items.Count > 100) LogList.Items.RemoveAt(LogList.Items.Count - 1);
            StatusText.Text = r.Success
                ? string.Format(Strings.Kb_SentFormat, _sentCount, serial)
                : string.Format(Strings.Common_FailedFormat, r.Combined.Trim());
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Strings.Kb_SendFailedFormat, ex.Message);
        }
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    /// <summary>双引号包裹并对设备 shell 的特殊字符转义（$ ` \ "）。</summary>
    private static string QuoteText(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '$' => "\\$",
                '"' => "\\\"",
                '`' => "\\`",
                _ => c,
            });
        }
        sb.Append('"');
        return sb.ToString();
    }
}
