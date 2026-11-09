using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AdbToolkit.Models;
using AdbToolkit.Resources;

namespace AdbToolkit.Services;

public record AdbResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}{Environment.NewLine}{StdErr}";
}

/// <summary>
/// adb.exe 的进程级封装。所有功能只通过 adb 命令实现，不依赖其他外部工具。
/// </summary>
public partial class AdbService
{
    public string AdbPath { get; set; } = "adb";

    [GeneratedRegex(@"^(\S+)\s+(device|offline|unauthorized|recovery|bootloader|sideload)")]
    private static partial Regex DeviceLineRegex();

    [GeneratedRegex(@"(\w+):([^\s]+)")]
    private static partial Regex DevicePropRegex();

    [GeneratedRegex(@"^(\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+(\d+)\s+(\d+)\s+([VDIWEF])\s+(.+?)\s*:\s(.*)$")]
    internal static partial Regex LogcatLineRegex();

    [GeneratedRegex(@"^\[(.+?)\]:\s\[(.*)\]$")]
    private static partial Regex GetPropLineRegex();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s+(\S+)\s+\(")]
    private static partial Regex DropBoxLineRegex();

    /// <summary>解析前台应用：dumpsys 输出中形如 "包名/Activity" 的组件 token。</summary>
    internal static ForegroundInfo? ExtractForeground(string rawOutput, string source)
    {
        foreach (var rawToken in rawOutput.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // 去掉窗口行尾部粘连的 "}:" 等
            var token = rawToken.TrimEnd('}', ')', ':', ',', ';');
            var idx = token.IndexOf('/');
            if (idx <= 0) continue;
            var pkg = token[..idx];
            // 包名至少含一个点；排除 "Window{"、"u0"、"A=10345:com.x"（Android 12+ topResumedActivity 附加字段）等噪音
            if (!pkg.Contains('.') || pkg.Contains('{') || pkg.Contains(':') || pkg.Contains('=')) continue;
            var act = token[(idx + 1)..];
            if (act.Length == 0) continue;
            return new ForegroundInfo(pkg, token, source);
        }
        return null;
    }

    /// <summary>
    /// 获取当前前台应用与页面。两级探测：
    /// 1) Activity 栈顶（topResumedActivity / mResumedActivity / 老版本 mFocusedActivity）
    /// 2) 兜底：焦点窗口 mCurrentFocus（弹窗/对话框场景）
    /// </summary>
    public async Task<ForegroundInfo?> GetForegroundAsync(string serial, CancellationToken ct = default)
    {
        var act = await RunShellAsync(
            "dumpsys activity activities | grep -E 'topResumedActivity|mResumedActivity|mFocusedActivity'",
            serial, ct);
        var fg = ExtractForeground(act.StdOut, "Activity");
        if (fg != null) return fg;

        var win = await RunShellAsync("dumpsys window | grep mCurrentFocus", serial, ct);
        return ExtractForeground(win.StdOut, "Window");
    }

    /// <summary>
    /// 获取当前所有可见窗口（含 SystemUI/桌面等系统窗口）。
    /// 依据：窗口有 Surface（mHasSurface=true）且视图可见（mViewVisibility=0x0）。
    /// </summary>
    public async Task<List<ForegroundInfo>> GetVisibleWindowsAsync(string serial, CancellationToken ct = default)
    {
        const string grep = "grep -E 'Window #[0-9]+|mHasSurface=true|mViewVisibility=0x0'";

        var r = await RunShellAsync($"dumpsys window windows | {grep}", serial, ct);
        var list = ParseVisibleWindows(r.StdOut);
        if (list.Count == 0)
        {
            // 兜底：老版本或子命令不可用时直接 dump 全量窗口
            var r2 = await RunShellAsync($"dumpsys window | {grep}", serial, ct);
            list = ParseVisibleWindows(r2.StdOut);
        }
        return list;
    }

    internal static List<ForegroundInfo> ParseVisibleWindows(string output)
    {
        var result = new List<ForegroundInfo>();
        string? currentLine = null;
        var hasSurface = false;
        var viewVisible = false;

        void Flush()
        {
            if (currentLine == null) return;
            if (hasSurface && viewVisible)
            {
                var fg = ExtractForeground(currentLine, Strings.DevMgr_SourceWindow);
                if (fg != null) result.Add(fg);
            }
            currentLine = null;
            hasSurface = false;
            viewVisible = false;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Contains("Window #") && line.Contains("Window{"))
            {
                Flush();
                currentLine = line;
            }
            else if (currentLine != null)
            {
                if (line.Contains("mHasSurface=true")) hasSurface = true;
                else if (line.Contains("mViewVisibility=0x0")) viewVisible = true;
            }
        }
        Flush();

        // dump 顺序为底层→顶层，反转后最上层窗口在前
        result.Reverse();
        return result;
    }

    /// <summary>执行 adb 命令并等待结束。</summary>
    public async Task<AdbResult> RunAsync(string args, string? serial = null, CancellationToken ct = default)
    {
        var fullArgs = serial is null ? args : $"-s {serial} {args}";
        return await RunCoreAsync(fullArgs, ct);
    }

    /// <summary>执行 adb shell 命令并等待结束。</summary>
    public Task<AdbResult> RunShellAsync(string command, string? serial = null, CancellationToken ct = default)
        => RunAsync($"shell {command}", serial, ct);

    private async Task<AdbResult> RunCoreAsync(string args, CancellationToken ct)
    {
        var psi = CreatePsi(args);
        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                string.Format(Strings.Adb_CannotStartFormat, AdbPath), ex);
        }

        var outTask = process.StandardOutput.ReadToEndAsync(ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 取消/超时时结束进程树，避免 adb 客户端残留
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return new AdbResult(process.ExitCode, await outTask, await errTask);
    }

    private ProcessStartInfo CreatePsi(string args) => new()
    {
        FileName = AdbPath,
        Arguments = args,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        // adb 输出为 UTF-8，避免中文乱码
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

    /// <summary>
    /// 启动一个长驻 adb shell 进程（monkey / screenrecord 等），onLine 在线程池线程回调。
    /// </summary>
    public Process StartShellProcess(string serial, string command,
        Action<string>? onLine = null, Action<int>? onExit = null)
    {
        var psi = CreatePsi($"-s {serial} shell {command}");
        var process = new Process { StartInfo = psi };

        if (onLine != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        }
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            try { onExit?.Invoke(process.ExitCode); }
            catch { /* 进程已退出时 ExitCode 可能不可用 */ }
        };
        process.Start();
        if (onLine != null)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        return process;
    }

    // ==================== 设备管理 ====================

    /// <summary>解析 adb devices -l 输出。</summary>
    public async Task<List<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var result = await RunAsync("devices -l", null, ct);
        var devices = new List<AdbDevice>();
        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            var m = DeviceLineRegex().Match(line);
            if (!m.Success) continue;

            var device = new AdbDevice
            {
                Serial = m.Groups[1].Value,
                State = m.Groups[2].Value switch
                {
                    "device" => AdbDeviceState.Device,
                    "offline" => AdbDeviceState.Offline,
                    "unauthorized" => AdbDeviceState.Unauthorized,
                    "recovery" => AdbDeviceState.Recovery,
                    "bootloader" => AdbDeviceState.Bootloader,
                    "sideload" => AdbDeviceState.Sideload,
                    _ => AdbDeviceState.Unknown,
                },
            };

            foreach (Match p in DevicePropRegex().Matches(line))
            {
                switch (p.Groups[1].Value)
                {
                    case "model": device.Model = p.Groups[2].Value; break;
                    case "product": device.Product = p.Groups[2].Value; break;
                    case "transport_id": device.TransportId = p.Groups[2].Value; break;
                }
            }

            devices.Add(device);
        }
        return devices;
    }

    /// <summary>根据包名取 PID（logcat --pid 过滤用）。</summary>
    public async Task<int?> PidOfAsync(string serial, string package, CancellationToken ct = default)
    {
        var result = await RunShellAsync($"pidof -s {package}", serial, ct);
        return int.TryParse(result.StdOut.Trim(), out var pid) ? pid : null;
    }

    // ==================== 文件传输 ====================

    public Task<AdbResult> PushAsync(string serial, string localPath, string devicePath, CancellationToken ct = default)
        => RunAsync($"push {Quote(localPath)} {Quote(devicePath)}", serial, ct);

    public Task<AdbResult> PullAsync(string serial, string devicePath, string localPath, CancellationToken ct = default)
        => RunAsync($"pull {Quote(devicePath)} {Quote(localPath)}", serial, ct);

    /// <summary>列出设备目录（ls -la 解析，兼容 toybox/toolbox 两种格式）。</summary>
    public async Task<List<DeviceFile>> ListDirectoryAsync(string serial, string path, CancellationToken ct = default)
    {
        var result = await RunShellAsync($"ls -la {Quote(path)}", serial, ct);
        var files = new List<DeviceFile>();
        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 7 || tokens[0].Length < 10 ||
                !"-dlbcps".Contains(tokens[0][0])) continue;

            // 兼容带/不带硬链接数的两种格式
            var i = 1;
            if (tokens.Length >= 8 && uint.TryParse(tokens[1], out var links) && links <= 1024) i = 2;
            if (tokens.Length < i + 5) continue;
            if (!long.TryParse(tokens[i + 2], out var size)) continue;

            // 名称取时间之后的整段，保留空格
            var timeToken = tokens[i + 4];
            var timeEnd = line.IndexOf(timeToken, StringComparison.Ordinal);
            if (timeEnd < 0) continue;
            var name = line[(timeEnd + timeToken.Length)..].TrimStart();
            if (name.Length == 0 || name == "." || name == "..") continue;

            var symlinkTarget = "";
            if (name.Contains(" -> "))
            {
                var parts = name.Split(new[] { " -> " }, 2, StringSplitOptions.None);
                name = parts[0];
                symlinkTarget = parts.Length > 1 ? parts[1] : "";
            }

            files.Add(new DeviceFile
            {
                Perms = tokens[0],
                IsDir = tokens[0][0] == 'd',
                IsSymlink = tokens[0][0] == 'l',
                Size = size,
                Date = $"{tokens[i + 3]} {timeToken}",
                Name = name,
            });
        }

        if (files.Count == 0 && !result.Success)
            throw new InvalidOperationException(string.Format(Strings.Adb_ListDirFailedFormat, result.Combined.Trim()));

        return files
            .OrderByDescending(f => f.IsDir || f.IsSymlink) // 目录（含软链目录）排前
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ==================== 截图 / 录屏 ====================

    /// <summary>截图并返回 PNG 字节（必须走 exec-out，shell 会破坏二进制）。</summary>
    public async Task<byte[]> CaptureScreenAsync(string serial, CancellationToken ct = default)
    {
        var psi = CreatePsi($"-s {serial} exec-out screencap -p");
        using var process = new Process { StartInfo = psi };
        process.Start();
        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0 || ms.Length == 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException(string.Format(Strings.Adb_ScreenshotFailedFormat, err.Trim()));
        }
        return ms.ToArray();
    }

    /// <summary>启动录屏。到时自动结束；提前停止请 Kill 进程树（设备端会正常收尾生成文件）。</summary>
    public Process StartScreenrecord(string serial, string devicePath,
        int bitRateMbps = 8, int timeLimitSec = 180, string? size = null, Action<int>? onExit = null)
    {
        var sb = new StringBuilder($"screenrecord --bit-rate {bitRateMbps * 1000000} --time-limit {timeLimitSec}");
        if (!string.IsNullOrWhiteSpace(size)) sb.Append($" --size {size}");
        sb.Append($" {Quote(devicePath)}");
        return StartShellProcess(serial, sb.ToString(), null, onExit);
    }

    // ==================== Logcat ====================

    /// <summary>启动 logcat 长驻流。onLine 在线程池线程回调，调用方自行调度到 UI 线程。</summary>
    public Process StartLogcat(string serial, IReadOnlyList<string> buffers, char minLevel,
        Action<string> onLine, Action<int>? onExit = null)
    {
        var sb = new StringBuilder($"logcat -v threadtime");
        foreach (var b in buffers) sb.Append($" -b {b}");
        sb.Append($" *:{minLevel}");
        return StartShellProcess(serial, sb.ToString(), onLine, onExit);
    }

    /// <summary>清空设备端 logcat 缓冲区。</summary>
    public Task<AdbResult> ClearLogcatAsync(string serial, CancellationToken ct = default)
        => RunShellAsync("logcat -b all -c", serial, ct);

    // ==================== 应用管理 ====================

    /// <summary>列出应用包。system=false 列第三方（-3），true 列系统应用（-s）。</summary>
    public async Task<List<AppInfo>> ListPackagesAsync(string serial, bool system, CancellationToken ct = default)
    {
        var flag = system ? "-s" : "-3";
        var result = await RunAsync($"shell pm list packages -f {flag}", serial, ct);
        var apps = new List<AppInfo>();
        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (!line.StartsWith("package:")) continue;
            var body = line["package:".Length..];
            var idx = body.LastIndexOf('=');
            if (idx < 0) continue;
            apps.Add(new AppInfo
            {
                ApkPath = body[..idx],
                Package = body[(idx + 1)..],
                IsSystem = system,
            });
        }
        return apps.OrderBy(a => a.Package, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<AdbResult> InstallAsync(string serial, string apkPath, bool replace = true,
        bool grantPermissions = false, bool allowTest = false, CancellationToken ct = default)
    {
        var sb = new StringBuilder("install");
        if (replace) sb.Append(" -r");
        if (grantPermissions) sb.Append(" -g");
        if (allowTest) sb.Append(" -t");
        sb.Append(' ').Append(Quote(apkPath));
        return RunAsync(sb.ToString(), serial, ct);
    }

    public Task<AdbResult> UninstallAsync(string serial, string package, bool keepData = false, CancellationToken ct = default)
        => RunAsync($"uninstall{(keepData ? " -k" : "")} {package}", serial, ct);

    /// <summary>查询应用的全部 APK 路径（App Bundle 安装的应用会有 base + 多个 split）。</summary>
    public async Task<List<string>> GetApkPathsAsync(string serial, string package, CancellationToken ct = default)
    {
        var r = await RunShellAsync($"pm path {package}", serial, ct);
        return r.StdOut.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("package:"))
            .Select(l => l["package:".Length..])
            .Where(p => p.Length > 0)
            .ToList();
    }

    // ==================== 属性 / 设置 ====================

    /// <summary>getprop 全量属性（[key]: [value] 解析）。</summary>
    public async Task<List<KeyValuePair<string, string>>> ListPropsAsync(string serial, CancellationToken ct = default)
    {
        var result = await RunShellAsync("getprop", serial, ct);
        var props = new List<KeyValuePair<string, string>>();
        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var m = GetPropLineRegex().Match(rawLine.TrimEnd('\r'));
            if (m.Success)
                props.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
        }
        return props.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>settings 列表（system/secure/global，key=value）。</summary>
    public async Task<List<KeyValuePair<string, string>>> ListSettingsAsync(string serial, string ns, CancellationToken ct = default)
    {
        var result = await RunShellAsync($"settings list {ns}", serial, ct);
        var items = new List<KeyValuePair<string, string>>();
        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            items.Add(new KeyValuePair<string, string>(line[..idx], line[(idx + 1)..]));
        }
        return items.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>DropBox 历史条目（崩溃/ANR 列表）。</summary>
    public async Task<List<DropBoxEntry>> ListDropBoxAsync(string serial, CancellationToken ct = default)
    {
        var result = await RunShellAsync("dumpsys dropbox", serial, ct);
        var entries = new List<DropBoxEntry>();
        foreach (var rawLine in result.StdOut.Split('\n'))
        {
            var m = DropBoxLineRegex().Match(rawLine.TrimEnd('\r'));
            if (m.Success)
                entries.Add(new DropBoxEntry { Time = m.Groups[1].Value, Tag = m.Groups[2].Value });
        }
        return entries;
    }

    // ==================== 工具 ====================

    private static string Quote(string path) =>
        string.IsNullOrWhiteSpace(path) || (!path.Contains(' ') && !path.Contains('\t'))
            ? path
            : $"\"{path}\"";

    /// <summary>自动探测 adb.exe：设置路径 → 程序目录 → LOCALAPPDATA SDK → PATH。</summary>
    public static string DetectAdbPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"),
            Path.Combine(AppContext.BaseDirectory, "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android", "Sdk", "platform-tools", "adb.exe"),
        };

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { candidates.Add(Path.Combine(dir, "adb.exe")); }
            catch { /* 忽略非法 PATH 项 */ }
        }

        return candidates.FirstOrDefault(File.Exists) ?? "adb";
    }
}
