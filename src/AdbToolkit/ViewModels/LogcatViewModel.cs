using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using AdbToolkit.Models;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>Logcat 查看器：抓取、着色、过滤、保存。</summary>
public class LogcatViewModel : ObservableObject
{
    /// <summary>每个刷新周期最多上屏的行数（高频日志时保护 UI 线程）。</summary>
    private const int MaxFlushPerTick = 300;

    /// <summary>屏显最大行数，超过后移除最老的。</summary>
    private const int MaxDisplayEntries = 5000;

    private readonly List<LogEntry> _all = new();
    private readonly Queue<LogEntry> _pending = new();
    private readonly object _sync = new();
    private readonly DispatcherTimer _flushTimer;

    private Process? _process;
    private long _droppedDisplay;

    public LogcatViewModel()
    {
        FilteredView = CollectionViewSource.GetDefaultView(Entries);
        FilteredView.Filter = FilterEntry;

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _flushTimer.Tick += (_, _) => FlushPending();

        Buffers = new List<string> { "all", "main", "system", "crash", "events", "kernel", "radio" };
        Levels = new List<string> { "V", "D", "I", "W", "E" };

        StartCommand = new RelayCommand(StartCapture, () => !IsCapturing);
        StopCommand = new RelayCommand(StopCapture, () => IsCapturing);
        ClearCommand = new RelayCommand(async () => await ClearAsync(), () => !IsCapturing);
        SaveCommand = new RelayCommand(SaveToFile, () => _all.Count > 0);
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ICollectionView FilteredView { get; }

    public List<string> Buffers { get; }

    public List<string> Levels { get; }

    private string _selectedBuffer = "main";
    public string SelectedBuffer
    {
        get => _selectedBuffer;
        set => SetProperty(ref _selectedBuffer, value);
    }

    private string _selectedLevel = "V";
    public string SelectedLevel
    {
        get => _selectedLevel;
        set => SetProperty(ref _selectedLevel, value);
    }

    private string _tagFilter = "";
    public string TagFilter
    {
        get => _tagFilter;
        set
        {
            if (SetProperty(ref _tagFilter, value)) FilteredView.Refresh();
        }
    }

    private string _keyword = "";
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value)) FilteredView.Refresh();
        }
    }

    private bool _autoScroll = true;
    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        private set => SetProperty(ref _isCapturing, value);
    }

    private string _status = "未开始抓取";
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand SaveCommand { get; }

    // ---------- 抓取 ----------

    private void StartCapture()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null)
        {
            Status = "请先在顶部选择设备";
            return;
        }

        StopCapture();
        lock (_sync) _all.Clear();
        Entries.Clear();

        var buffers = SelectedBuffer == "all"
            ? new[] { "all" }
            : new[] { SelectedBuffer };

        try
        {
            _process = AppSession.Adb.StartLogcat(
                serial, buffers, SelectedLevel[0],
                OnLineReceived,
                code =>
                {
                    // onExit 来自线程池线程，需调度回 UI 线程
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        IsCapturing = false;
                        _flushTimer.Stop();
                        Status = $"logcat 进程已退出（code {code}）";
                    });
                });
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return;
        }

        IsCapturing = true;
        _flushTimer.Start();
        Status = $"正在抓取（设备 {serial} · buffer {SelectedBuffer} · *:{SelectedLevel}）";
    }

    private void OnLineReceived(string line)
    {
        var entry = ParseLine(line);
        lock (_sync) _pending.Enqueue(entry);
    }

    internal static LogEntry ParseLine(string line)
    {
        var m = AdbService.LogcatLineRegex().Match(line);
        if (!m.Success)
        {
            return new LogEntry { Raw = line, Message = line, Level = 'I' };
        }
        return new LogEntry
        {
            Time = m.Groups[1].Value,
            Pid = int.TryParse(m.Groups[2].Value, out var pid) ? pid : 0,
            Tid = int.TryParse(m.Groups[3].Value, out var tid) ? tid : 0,
            Level = m.Groups[4].Value[0],
            Tag = m.Groups[5].Value.Trim(),
            Message = m.Groups[6].Value,
        };
    }

    /// <summary>
    /// 把进程线程收到的行批量搬到 UI 集合。
    /// 限流：每周期最多上屏 MaxFlushPerTick 行，过载时丢最老的显示行，防止 UI 线程被打爆。
    /// </summary>
    private void FlushPending()
    {
        LogEntry[] batch;
        long droppedNow;
        lock (_sync)
        {
            if (_pending.Count == 0) return;
            // 过载保护：积压超过 3 个周期的量时，只保留最新的 MaxFlushPerTick 行
            droppedNow = 0;
            if (_pending.Count > MaxFlushPerTick * 3)
            {
                droppedNow = _pending.Count - MaxFlushPerTick;
                for (var i = 0; i < droppedNow; i++) _pending.Dequeue();
            }
            var n = Math.Min(_pending.Count, MaxFlushPerTick);
            batch = new LogEntry[n];
            for (var i = 0; i < n; i++) batch[i] = _pending.Dequeue();
        }
        _droppedDisplay += droppedNow;

        lock (_sync)
        {
            _all.AddRange(batch);
            // 限制内存占用（环形缓冲）
            var overflow = _all.Count - AppSession.Settings.LogcatBufferLines;
            if (overflow > 0) _all.RemoveRange(0, overflow);
        }

        foreach (var e in batch) Entries.Add(e);

        // 屏显裁剪（保存仍取 _all，两者独立）
        var displayOverflow = Entries.Count - MaxDisplayEntries;
        if (displayOverflow > 0)
        {
            for (var i = 0; i < displayOverflow; i++) Entries.RemoveAt(0);
        }

        Status = _droppedDisplay > 0
            ? $"正在抓取 · 缓冲 {_all.Count:N0} 行（屏显过载丢弃 {_droppedDisplay:N0} 行）"
            : $"正在抓取 · 缓冲 {_all.Count:N0} 行";
    }

    private void StopCapture()
    {
        var p = _process;
        _process = null;
        IsCapturing = false;
        _flushTimer.Stop();
        if (p is null) return;

        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch { /* 可能已退出 */ }
        finally
        {
            p.Dispose();
        }
    }

    private async Task ClearAsync()
    {
        var serial = DeviceStore.SelectedSerial;
        if (serial is null) return;
        await AppSession.Adb.ClearLogcatAsync(serial);
        lock (_sync) _all.Clear();
        Entries.Clear();
        Status = "已清空设备端日志缓冲区";
    }

    // ---------- 过滤 ----------

    private bool FilterEntry(object obj)
    {
        if (obj is not LogEntry e) return false;

        if (TagFilter.Length > 0 &&
            e.Tag.IndexOf(TagFilter, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (Keyword.Length > 0 &&
            e.Message.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0 &&
            e.Tag.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return true;
    }

    // ---------- 保存 ----------

    private void SaveToFile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"logcat_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "文本文件 (*.txt)|*.txt|日志文件 (*.log)|*.log",
        };
        if (dialog.ShowDialog() != true) return;

        LogEntry[] snapshot;
        lock (_sync) snapshot = _all.ToArray();

        var sb = new StringBuilder();
        foreach (var e in snapshot)
        {
            sb.AppendLine(e.Raw.Length > 0
                ? e.Raw
                : $"{e.Time} {e.Pid,5} {e.Tid,5} {e.Level} {e.Tag}: {e.Message}");
        }
        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        Status = $"已保存 {snapshot.Length:N0} 行到 {dialog.FileName}";
    }

    /// <summary>离开页面时由 View 调用，确保子进程被回收。</summary>
    public void Shutdown() => StopCapture();
}
