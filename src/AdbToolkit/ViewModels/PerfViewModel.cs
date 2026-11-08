using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using AdbToolkit.Models;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>性能诊断页：设备信息、电量、内存、启动时间、帧率、硬件状态（sysfs）、崩溃历史。</summary>
public partial class PerfViewModel : ObservableObject
{
    [GeneratedRegex(@"Total frames rendered:\s*(\d+)")]
    private static partial Regex GfxTotalRegex();

    [GeneratedRegex(@"Janky frames:\s*(\d+)\s*\(([\d.]+)%\)")]
    private static partial Regex GfxJankyRegex();

    [GeneratedRegex(@"TotalTime:\s*(\d+)")]
    private static partial Regex TotalTimeRegex();

    [GeneratedRegex(@"MemTotal:\s+(\d+)\s+kB")]
    private static partial Regex MemTotalRegex();

    [GeneratedRegex(@"inet\s+(\d+(?:\.\d+){3})")]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"level:\s*(\d+)")]
    private static partial Regex BatteryLevelRegex();

    [GeneratedRegex(@"Physical density:\s*(\d+)")]
    private static partial Regex PhysicalDensityRegex();

    [GeneratedRegex(@"Override density:\s*(\d+)")]
    private static partial Regex OverrideDensityRegex();

    [GeneratedRegex(@"versionName=(\S+)")]
    private static partial Regex VersionNameRegex();

    [GeneratedRegex(@"versionCode=(\d+)")]
    private static partial Regex VersionCodeRegex();

    [GeneratedRegex(@"minSdk=(\d+)")]
    private static partial Regex MinSdkRegex();

    [GeneratedRegex(@"targetSdk=(\d+)")]
    private static partial Regex TargetSdkRegex();

    [GeneratedRegex(@"codePath=(\S+)")]
    private static partial Regex CodePathRegex();

    [GeneratedRegex(@"firstInstallTime=([0-9\- :]+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex FirstInstallRegex();

    [GeneratedRegex(@"lastUpdateTime=([0-9\- :]+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex LastUpdateRegex();

    [GeneratedRegex(@"installerPackageName=(\S+)")]
    private static partial Regex InstallerRegex();

    [GeneratedRegex(@"pkgFlags=\[([^\]]*)\]")]
    private static partial Regex PkgFlagsRegex();

    private readonly DispatcherTimer _autoTimer;

    public PerfViewModel()
    {
        _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoTimer.Tick += async (_, _) => await RefreshBatteryAndHardwareAsync();

        LoadDeviceInfoCommand = new RelayCommand(async () => await RunBusyAsync(LoadDeviceInfoAsync));
        RefreshBatteryCommand = new RelayCommand(async () => await RunBusyAsync(RefreshBatteryAsync));
        AutoRefreshCommand = new RelayCommand(() => AutoRefresh = !AutoRefresh);
        SetBatteryLevelCommand = new RelayCommand(async () => await RunBusyAsync(SetBatteryLevelAsync));
        BatteryResetCommand = new RelayCommand(async () => await RunBusyAsync(BatteryResetAsync));
        BatteryUnplugCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            await RunShell("dumpsys battery unplug");
            await RefreshBatteryAsync();
        }));
        MemInfoCommand = new RelayCommand(async () => await RunBusyAsync(LoadMemInfoAsync));
        LoadAppDetailCommand = new RelayCommand(async () => await RunBusyAsync(LoadAppDetailAsync));
        OpenDetailSettingsCommand = new RelayCommand(async () => await RunBusyAsync(OpenDetailSettingsAsync));
        StartupTestCommand = new RelayCommand(async () => await RunBusyAsync(StartupTestAsync));
        GfxInfoCommand = new RelayCommand(async () => await RunBusyAsync(LoadGfxInfoAsync));
        RefreshHardwareCommand = new RelayCommand(async () => await RunBusyAsync(RefreshBatteryAndHardwareAsync));
        RefreshDropBoxCommand = new RelayCommand(async () => await RunBusyAsync(LoadDropBoxAsync));
        ViewDropBoxCommand = new RelayCommand(async () => await RunBusyAsync(ViewSelectedDropBoxAsync));
    }

    // ---------- 设备信息 ----------
    private string _deviceInfo = "";
    public string DeviceInfo { get => _deviceInfo; private set => SetProperty(ref _deviceInfo, value); }

    // ---------- 电量 ----------
    private string _batteryText = "";
    public string BatteryText { get => _batteryText; private set => SetProperty(ref _batteryText, value); }
    private string _virtualLevel = "15";
    public string VirtualLevel { get => _virtualLevel; set => SetProperty(ref _virtualLevel, value); }
    private bool _autoRefresh;
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (SetProperty(ref _autoRefresh, value))
            {
                if (value) _autoTimer.Start(); else _autoTimer.Stop();
            }
        }
    }

    // ---------- 内存 ----------
    private string _memPackage = "";
    public string MemPackage { get => _memPackage; set => SetProperty(ref _memPackage, value); }
    private string _memInfoText = "";
    public string MemInfoText { get => _memInfoText; private set => SetProperty(ref _memInfoText, value); }

    // ---------- 启动时间 ----------
    private string _startupComponent = "";
    public string StartupComponent { get => _startupComponent; set => SetProperty(ref _startupComponent, value); }
    private string _startupCount = "5";
    public string StartupCount { get => _startupCount; set => SetProperty(ref _startupCount, value); }
    private string _startupResult = "";
    public string StartupResult { get => _startupResult; private set => SetProperty(ref _startupResult, value); }

    // ---------- 帧率 ----------
    private string _gfxPackage = "";
    public string GfxPackage { get => _gfxPackage; set => SetProperty(ref _gfxPackage, value); }
    private string _gfxResult = "";
    public string GfxResult { get => _gfxResult; private set => SetProperty(ref _gfxResult, value); }

    // ---------- 硬件状态 ----------
    public ObservableCollection<string> CpuFreqs { get; } = new();
    public ObservableCollection<string> GpuFreqs { get; } = new();
    public ObservableCollection<string> ThermalZones { get; } = new();

    // ---------- 崩溃历史 ----------
    public ObservableCollection<DropBoxEntry> DropBoxEntries { get; } = new();
    private DropBoxEntry? _selectedDropBox;
    public DropBoxEntry? SelectedDropBox
    {
        get => _selectedDropBox;
        set => SetProperty(ref _selectedDropBox, value);
    }
    private string _dropBoxText = "";
    public string DropBoxText { get => _dropBoxText; private set => SetProperty(ref _dropBoxText, value); }

    private bool _busy;
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    private string _status = "就绪";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public RelayCommand LoadDeviceInfoCommand { get; }
    public RelayCommand RefreshBatteryCommand { get; }
    public RelayCommand AutoRefreshCommand { get; }
    public RelayCommand SetBatteryLevelCommand { get; }
    public RelayCommand BatteryResetCommand { get; }
    public RelayCommand BatteryUnplugCommand { get; }
    public RelayCommand MemInfoCommand { get; }
    public RelayCommand LoadAppDetailCommand { get; }
    public RelayCommand OpenDetailSettingsCommand { get; }
    public RelayCommand StartupTestCommand { get; }
    public RelayCommand GfxInfoCommand { get; }
    public RelayCommand RefreshHardwareCommand { get; }
    public RelayCommand RefreshDropBoxCommand { get; }
    public RelayCommand ViewDropBoxCommand { get; }

    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await work(); }
        catch (Exception ex) { Status = "错误：" + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task<AdbResult> RunShell(string cmd)
    {
        var serial = DeviceStore.SelectedSerial ?? throw new InvalidOperationException("请先在顶部选择设备");
        return await AppSession.Adb.RunShellAsync(cmd, serial);
    }

    // ---------- 实现 ----------

    public async Task LoadOnNavigateAsync()
    {
        if (DeviceInfo.Length > 0) return;
        await RunBusyAsync(async () =>
        {
            await LoadDeviceInfoAsync();
            await RefreshBatteryAndHardwareAsync();
        });
    }

    private async Task LoadDeviceInfoAsync()
    {
        var props = await AppSession.Adb.ListPropsAsync(DeviceStore.SelectedSerial!);
        var dict = props.ToDictionary(p => p.Key, p => p.Value);
        string Get(string key) => dict.TryGetValue(key, out var v) && v.Length > 0 ? v : "—";

        var size = await RunShell("wm size");
        var density = await RunShell("wm density");
        var kernelRaw = await RunShell("cat /proc/version");
        var memRaw = await RunShell("cat /proc/meminfo");
        var dfRaw = await RunShell("df -h /data /storage/emulated 2>/dev/null");
        var wlan0 = await RunShell("ip -f inet addr show wlan0 2>/dev/null");
        var eth0 = await RunShell("ip -f inet addr show eth0 2>/dev/null");
        var battRaw = await RunShell("dumpsys battery");

        // 内存总量
        var memText = "—";
        var mem = MemTotalRegex().Match(memRaw.StdOut);
        if (mem.Success && long.TryParse(mem.Groups[1].Value, out var kbTotal))
            memText = DeviceFile.FormatSize(kbTotal * 1024L);

        // WLAN / 有线 IP
        var wlan = IpRegex().Match(wlan0.StdOut);
        var ip = wlan.Success ? wlan.Groups[1].Value : "";
        if (ip.Length == 0)
        {
            var eth = IpRegex().Match(eth0.StdOut);
            if (eth.Success) ip = eth.Groups[1].Value;
        }

        // 电量
        var batt = BatteryLevelRegex().Match(battRaw.StdOut);

        var kernelLine = kernelRaw.StdOut.Split('\n').FirstOrDefault()?.Trim() ?? "";
        if (kernelLine.Length > 100) kernelLine = kernelLine[..100] + "…";

        var dfLines = dfRaw.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.Contains("Filesystem") && l.Contains('/'))
            .Select(l => l.Trim())
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"机型：        {Get("ro.product.model")}（{Get("ro.product.brand")} / {Get("ro.product.manufacturer")}）");
        sb.AppendLine($"设备代号：    {Get("ro.product.device")}（{Get("ro.product.name")}）");
        sb.AppendLine($"Android 版本：{Get("ro.build.version.release")}（SDK {Get("ro.build.version.sdk")}）");
        sb.AppendLine($"系统版本号：  {Get("ro.build.display.id")}");
        sb.AppendLine($"构建：        {Get("ro.build.id")} · {Get("ro.build.version.incremental")}");
        sb.AppendLine($"序列号：      {Get("ro.serialno")}");
        sb.AppendLine($"引导程序：    {Get("ro.bootloader")}");
        sb.AppendLine($"SoC：         {Get("ro.board.platform")} / {Get("ro.hardware")}");
        sb.AppendLine($"ABI 列表：    {Get("ro.product.cpu.abilist")}");
        sb.AppendLine($"内核：        {kernelLine}");
        sb.AppendLine($"安全补丁：    {Get("ro.build.version.security_patch")}");
        sb.AppendLine(size.StdOut.Trim().Replace("Physical size:", "屏幕分辨率："));
        sb.AppendLine(BuildDensityText(density.StdOut, Get("ro.sf.lcd_density")));
        sb.AppendLine($"IP 地址：     {(ip.Length > 0 ? ip : "—")}");
        sb.AppendLine($"内存总量：    {memText}");
        sb.AppendLine($"电池电量：    {(batt.Success ? batt.Groups[1].Value + "%" : "—")}");
        sb.AppendLine($"低内存设备：  {Get("ro.config.low_ram")}");
        sb.AppendLine($"Java 堆：     增长上限 {Get("dalvik.vm.heapgrowthlimit")} / 最大 {Get("dalvik.vm.heapsize")}");
        foreach (var l in dfLines) sb.AppendLine($"存储：        {l}");
        DeviceInfo = sb.ToString();
        Status = "设备信息已加载";
    }

    // ---------- 应用详情 ----------

    private string _detailPackage = "";
    public string DetailPackage { get => _detailPackage; set => SetProperty(ref _detailPackage, value); }

    private string _appDetailText = "";
    public string AppDetailText { get => _appDetailText; private set => SetProperty(ref _appDetailText, value); }

    private async Task LoadAppDetailAsync()
    {
        var pkg = DetailPackage.Trim();
        if (pkg.Length == 0) { Status = "请填写要查询的包名"; return; }

        Status = $"正在查询 {pkg} ...";
        var r = await RunShell($"dumpsys package {pkg}");
        if (r.StdOut.Contains("Unable to find package") || !VersionNameRegex().IsMatch(r.StdOut))
        {
            AppDetailText = $"未找到应用 {pkg}（请检查包名，或应用未安装）";
            Status = "查询失败：应用不存在";
            return;
        }

        var outText = r.StdOut;
        string G(Regex regex) => regex.Match(outText) is { Success: true } m ? m.Groups[1].Value : "—";

        // requested permissions 块
        var perms = new List<string>();
        var lines = outText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "requested permissions:") continue;
            for (var j = i + 1; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                var p = lines[j].Trim();
                if (p.Contains('.')) perms.Add(p);
            }
            break;
        }

        var flags = PkgFlagsRegex().Match(outText) is { Success: true } fm ? fm.Groups[1].Value : "";
        var flagsText = flags.Length == 0 ? "—" :
            string.Join(" ", flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(f => f is "DEBUGGABLE" or "SYSTEM" or "PERSISTENT" or "STOPPED" or "INSTALLED"));

        var sb = new StringBuilder();
        sb.AppendLine($"包名：        {pkg}");
        sb.AppendLine($"版本：        {G(VersionNameRegex())}（versionCode {G(VersionCodeRegex())}）");
        sb.AppendLine($"SDK：         minSdk {G(MinSdkRegex())} / targetSdk {G(TargetSdkRegex())}");
        sb.AppendLine($"APK 路径：    {G(CodePathRegex())}");
        sb.AppendLine($"首次安装：    {G(FirstInstallRegex()).Trim()}");
        sb.AppendLine($"最近更新：    {G(LastUpdateRegex()).Trim()}");
        sb.AppendLine($"安装来源：    {G(InstallerRegex())}");
        sb.AppendLine($"标志：        {flagsText}");
        sb.AppendLine($"权限（{perms.Count}）：");
        foreach (var p in perms.Take(40)) sb.AppendLine($"  {p}");
        if (perms.Count > 40) sb.AppendLine($"  …（其余 {perms.Count - 40} 项省略）");
        AppDetailText = sb.ToString();
        Status = $"{pkg} 详情已加载";
    }

    private async Task OpenDetailSettingsAsync()
    {
        var pkg = DetailPackage.Trim();
        if (pkg.Length == 0) { Status = "请填写包名"; return; }
        var r = await RunShell(
            $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{pkg}\"");
        Status = r.Success ? $"已打开 {pkg} 的系统详情页" : $"打开失败：{r.Combined.Trim()}";
    }

    /// <summary>密度数值归入资源档位（Android 按最近标准桶匹配）：ldpi 120 / mdpi 160 / hdpi 240 / xhdpi 320 / xxhdpi 480 / xxxhdpi 640。</summary>
    private static string DensityBucket(int dpi) => dpi switch
    {
        <= 120 => "ldpi",
        <= 160 => "mdpi",
        <= 240 => "hdpi",
        <= 320 => "xhdpi",
        <= 480 => "xxhdpi",
        _ => "xxxhdpi",
    };

    /// <summary>
    /// 组装屏幕密度显示行：优先 Override（开发者选项里改过），其次 Physical，
    /// 都没有时回退 ro.sf.lcd_density 属性；附带档位名（hdpi/xhdpi/xxhdpi…）。
    /// </summary>
    private static string BuildDensityText(string wmDensityOut, string lcdProp)
    {
        int? physical = null, active = null;
        var mP = PhysicalDensityRegex().Match(wmDensityOut);
        if (mP.Success) physical = int.Parse(mP.Groups[1].Value);
        var mO = OverrideDensityRegex().Match(wmDensityOut);
        if (mO.Success) active = int.Parse(mO.Groups[1].Value);
        active ??= physical;
        if (active == null && int.TryParse(lcdProp, out var propDpi)) active = propDpi;
        if (active == null) return "屏幕密度：    —";

        var text = $"屏幕密度：    {active} dpi（{DensityBucket(active.Value)} 档）";
        if (physical != null && active != physical) text += $"，已由开发者选项覆盖（物理 {physical}）";
        return text;
    }

    private async Task RefreshBatteryAsync()
    {
        var r = await RunShell("dumpsys battery");
        var dict = new Dictionary<string, string>();
        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.Trim();
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        if (dict.Count == 0) { BatteryText = r.Combined.Trim(); return; }

        string Get(string k) => dict.TryGetValue(k, out var v) ? v : "—";
        var status = Get("status") switch { "2" => "充电中", "3" => "放电中", "4" => "未充电", "5" => "已充满", _ => Get("status") };
        var health = Get("health") switch { "2" => "良好", "3" => "过热", "4" => "已损坏", "5" => "过压", "7" => "过冷", _ => Get("health") };
        var plugged = Get("plugged") switch { "0" => "未插入", "1" => "AC", "2" => "USB", "4" => "无线", _ => Get("plugged") };
        if (int.TryParse(Get("temperature"), out var t)) dict["temperature"] = $"{t / 10.0:F1} °C";

        BatteryText =
            $"电量：{Get("level")}%   状态：{status}   插入方式：{plugged}\n" +
            $"健康度：{health}   温度：{dict.GetValueOrDefault("temperature", "—")}   电压：{Get("voltage")} mV\n" +
            $"电池技术：{Get("technology")}   Charge counter：{Get("charge counter")}";
    }

    private async Task SetBatteryLevelAsync()
    {
        if (!int.TryParse(VirtualLevel, out var level)) return;
        await RunShell($"dumpsys battery set level {Math.Clamp(level, 1, 100)}");
        await RefreshBatteryAsync();
        Status = $"已模拟电量 {level}%（dumpsys battery reset 恢复）";
    }

    private async Task BatteryResetAsync()
    {
        await RunShell("dumpsys battery reset");
        await RefreshBatteryAsync();
        Status = "电量模拟已重置";
    }

    private async Task LoadMemInfoAsync()
    {
        var pkg = MemPackage.Trim();
        if (pkg.Length == 0) { Status = "请填写包名"; return; }
        var r = await RunShell($"dumpsys meminfo {pkg}");
        MemInfoText = r.StdOut.Trim();
        Status = r.Success ? $"已获取 {pkg} 内存信息" : $"失败：{r.Combined.Trim()}";
    }

    private async Task StartupTestAsync()
    {
        var comp = StartupComponent.Trim();
        if (comp.Length == 0) { Status = "请填写组件，例如 com.android.settings/.Settings"; return; }
        if (!int.TryParse(StartupCount, out var n)) n = 5;

        var times = new List<int>();
        var lines = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            var r = await RunShell($"am start -W -n {comp}");
            var m = TotalTimeRegex().Match(r.StdOut);
            if (m.Success)
            {
                var t = int.Parse(m.Groups[1].Value);
                times.Add(t);
                lines.AppendLine($"第 {i + 1} 次：{t} ms");
            }
            else
            {
                lines.AppendLine($"第 {i + 1} 次：失败（{r.Combined.Split('\n').FirstOrDefault()?.Trim()}）");
            }
            await Task.Delay(800);
        }
        if (times.Count > 0)
            lines.AppendLine($"—— 平均 {times.Average():F0} ms / 最快 {times.Min()} ms / 最慢 {times.Max()} ms ——");
        StartupResult = lines.ToString();
        Status = "启动时间测试完成";
    }

    private async Task LoadGfxInfoAsync()
    {
        var pkg = GfxPackage.Trim();
        if (pkg.Length == 0) { Status = "请填写包名"; return; }
        var r = await RunShell($"dumpsys gfxinfo {pkg}");
        var total = GfxTotalRegex().Match(r.StdOut);
        var janky = GfxJankyRegex().Match(r.StdOut);
        GfxResult = total.Success
            ? $"总帧数：{total.Groups[1].Value}\n卡顿帧：{janky.Groups[1].Value}（{janky.Groups[2].Value}%）"
            : r.Combined.Trim();
        Status = "帧率数据已获取（建议先操作目标应用产生帧数据）";
    }

    private async Task RefreshBatteryAndHardwareAsync()
    {
        await RefreshBatteryAsync();
        await LoadHardwareAsync();
    }

    private async Task LoadHardwareAsync()
    {
        // CPU 频率（glob 一次拉全，避免逐核查询）
        var cur = await RunShell("cat /sys/devices/system/cpu/cpufreq/policy*/scaling_cur_freq 2>/dev/null");
        var max = await RunShell("cat /sys/devices/system/cpu/cpufreq/policy*/scaling_max_freq 2>/dev/null");
        var names = await RunShell("ls /sys/devices/system/cpu/cpufreq/ 2>/dev/null");
        var curList = cur.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var maxList = max.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var policies = names.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.StartsWith("policy")).ToList();

        CpuFreqs.Clear();
        for (var i = 0; i < curList.Count; i++)
        {
            var name = i < policies.Count ? policies[i] : $"policy{i}";
            CpuFreqs.Add($"{name}：当前 {FmtFreq(curList[i])} / 上限 {FmtFreq(maxList.Count > i ? maxList[i] : "")}");
        }

        // GPU（devfreq 节点，路径因平台而异）
        var gpuNames = await RunShell("ls /sys/class/devfreq/ 2>/dev/null");
        var gpuCur = await RunShell("cat /sys/class/devfreq/*/cur_freq 2>/dev/null");
        var gpuNameList = gpuNames.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var gpuCurList = gpuCur.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        GpuFreqs.Clear();
        for (var i = 0; i < gpuNameList.Count; i++)
        {
            var freq = i < gpuCurList.Count ? FmtFreq(gpuCurList[i]) : "—";
            GpuFreqs.Add($"{gpuNameList[i]}：{freq}");
        }

        // 温度（type 与 temp 按 glob 顺序一一对应）
        var types = await RunShell("cat /sys/class/thermal/thermal_zone*/type 2>/dev/null");
        var temps = await RunShell("cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null");
        var typeList = types.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        var tempList = temps.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        ThermalZones.Clear();
        for (var i = 0; i < typeList.Count; i++)
        {
            var v = i < tempList.Count && double.TryParse(tempList[i], out var mv) ? $"{mv / 1000.0:F1} °C" : tempList.ElementAtOrDefault(i) ?? "";
            ThermalZones.Add($"{typeList[i]}：{v}");
        }
    }

    private static string FmtFreq(string raw)
    {
        if (!long.TryParse(raw.Trim(), out var khz)) return "—";
        return khz >= 1_000_000 ? $"{khz / 1_000_000.0:F2} GHz"
             : khz >= 1000 ? $"{khz / 1000.0:F0} MHz"
             : $"{khz} kHz";
    }

    private async Task LoadDropBoxAsync()
    {
        var entries = await AppSession.Adb.ListDropBoxAsync(DeviceStore.SelectedSerial!);
        DropBoxEntries.Clear();
        foreach (var e in entries.TakeLast(200)) DropBoxEntries.Add(e);
        Status = $"DropBox 共 {entries.Count} 条，显示最近 {Math.Min(200, entries.Count)} 条";
    }

    private async Task ViewSelectedDropBoxAsync()
    {
        if (SelectedDropBox is null) { Status = "请先选中一条记录"; return; }
        var r = await RunShell($"dumpsys dropbox --print {SelectedDropBox.Tag}");
        var text = r.StdOut.Trim();
        DropBoxText = text.Length > 20000 ? text[..20000] + "\n...(已截断)" : text;
        Status = $"已加载 {SelectedDropBox.Tag} 最新一条（时间戳可能多于所选）";
    }
}
