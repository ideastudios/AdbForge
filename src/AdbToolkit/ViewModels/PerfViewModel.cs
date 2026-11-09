using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using AdbToolkit.Models;
using AdbToolkit.Resources;
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
    private string _status = Strings.More_Ready;
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
        catch (Exception ex) { Status = string.Format(Strings.Common_ErrorFormat, ex.Message); }
        finally { IsBusy = false; }
    }

    private async Task<AdbResult> RunShell(string cmd)
    {
        var serial = DeviceStore.SelectedSerial ?? throw new InvalidOperationException(Strings.Common_SelectDeviceFirst);
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
        sb.AppendLine(string.Format(Strings.Perf_DevModelFormat, Get("ro.product.model"), Get("ro.product.brand"), Get("ro.product.manufacturer")));
        sb.AppendLine(string.Format(Strings.Perf_DevCodenameFormat, Get("ro.product.device"), Get("ro.product.name")));
        sb.AppendLine(string.Format(Strings.Perf_DevAndroidFormat, Get("ro.build.version.release"), Get("ro.build.version.sdk")));
        sb.AppendLine(string.Format(Strings.Perf_DevBuildIdFormat, Get("ro.build.display.id")));
        sb.AppendLine(string.Format(Strings.Perf_DevBuildFormat, Get("ro.build.id"), Get("ro.build.version.incremental")));
        sb.AppendLine(string.Format(Strings.Perf_DevSerialFormat, Get("ro.serialno")));
        sb.AppendLine(string.Format(Strings.Perf_DevBootloaderFormat, Get("ro.bootloader")));
        sb.AppendLine(string.Format(Strings.Perf_DevSocFormat, Get("ro.board.platform"), Get("ro.hardware")));
        sb.AppendLine(string.Format(Strings.Perf_DevAbiFormat, Get("ro.product.cpu.abilist")));
        sb.AppendLine(string.Format(Strings.Perf_DevKernelFormat, kernelLine));
        sb.AppendLine(string.Format(Strings.Perf_DevPatchFormat, Get("ro.build.version.security_patch")));
        sb.AppendLine(size.StdOut.Trim().Replace("Physical size:", Strings.Perf_ResolutionLabel + ": "));
        sb.AppendLine(BuildDensityText(density.StdOut, Get("ro.sf.lcd_density")));
        sb.AppendLine(string.Format(Strings.Perf_DevIpFormat, ip.Length > 0 ? ip : "—"));
        sb.AppendLine(string.Format(Strings.Perf_DevMemFormat, memText));
        sb.AppendLine(string.Format(Strings.Perf_DevBatteryFormat, batt.Success ? batt.Groups[1].Value + "%" : "—"));
        sb.AppendLine(string.Format(Strings.Perf_DevLowRamFormat, Get("ro.config.low_ram")));
        sb.AppendLine(string.Format(Strings.Perf_DevHeapFormat, Get("dalvik.vm.heapgrowthlimit"), Get("dalvik.vm.heapsize")));
        foreach (var l in dfLines) sb.AppendLine(string.Format(Strings.Perf_DevStorageFormat, l));
        DeviceInfo = sb.ToString();
        Status = Strings.Perf_DeviceInfoLoaded;
    }

    // ---------- 应用详情 ----------

    private string _detailPackage = "";
    public string DetailPackage { get => _detailPackage; set => SetProperty(ref _detailPackage, value); }

    private string _appDetailText = "";
    public string AppDetailText { get => _appDetailText; private set => SetProperty(ref _appDetailText, value); }

    private async Task LoadAppDetailAsync()
    {
        var pkg = DetailPackage.Trim();
        if (pkg.Length == 0) { Status = Strings.Perf_EnterPackage; return; }

        Status = string.Format(Strings.Perf_QueryingFormat, pkg);
        var r = await RunShell($"dumpsys package {pkg}");
        if (r.StdOut.Contains("Unable to find package") || !VersionNameRegex().IsMatch(r.StdOut))
        {
            AppDetailText = string.Format(Strings.Perf_AppNotFoundFormat, pkg);
            Status = Strings.Perf_QueryFailedNotFound;
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
        sb.AppendLine(string.Format(Strings.Apps_DtlPkgFormat, pkg));
        sb.AppendLine(string.Format(Strings.Apps_DtlVersionFormat, G(VersionNameRegex()), G(VersionCodeRegex())));
        sb.AppendLine(string.Format(Strings.Apps_DtlSdkFormat, G(MinSdkRegex()), G(TargetSdkRegex())));
        sb.AppendLine(string.Format(Strings.Apps_DtlApkPathFormat, G(CodePathRegex())));
        sb.AppendLine(string.Format(Strings.Apps_DtlFirstInstallFormat, G(FirstInstallRegex()).Trim()));
        sb.AppendLine(string.Format(Strings.Apps_DtlLastUpdateFormat, G(LastUpdateRegex()).Trim()));
        sb.AppendLine(string.Format(Strings.Apps_DtlInstallerFormat, G(InstallerRegex())));
        sb.AppendLine(string.Format(Strings.Apps_DtlFlagsFormat, flagsText));
        sb.AppendLine(string.Format(Strings.Perf_PermsHeaderFormat, perms.Count));
        foreach (var p in perms.Take(40)) sb.AppendLine($"  {p}");
        if (perms.Count > 40) sb.AppendLine(string.Format(Strings.Apps_SectionTruncatedFormat, perms.Count - 40));
        AppDetailText = sb.ToString();
        Status = string.Format(Strings.Perf_DetailLoadedFormat, pkg);
    }

    private async Task OpenDetailSettingsAsync()
    {
        var pkg = DetailPackage.Trim();
        if (pkg.Length == 0) { Status = Strings.Perf_EnterPackage; return; }
        var r = await RunShell(
            $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{pkg}\"");
        Status = r.Success
            ? string.Format(Strings.Apps_SystemDetailOpenedFormat, pkg)
            : string.Format(Strings.Common_OpenFailedFormat, r.Combined.Trim());
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
        if (active == null) return Strings.Perf_DensityNone;

        var text = string.Format(Strings.Perf_DensityFormat, active, DensityBucket(active.Value));
        if (physical != null && active != physical)
            text += string.Format(Strings.Perf_DensityOverriddenFormat, physical);
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
        var status = Get("status") switch
        {
            "2" => Strings.Perf_BattCharging,
            "3" => Strings.Perf_BattDischarging,
            "4" => Strings.Perf_BattNotCharging,
            "5" => Strings.Perf_BattFull,
            _ => Get("status"),
        };
        var health = Get("health") switch
        {
            "2" => Strings.Perf_BattGood,
            "3" => Strings.Perf_BattOverheat,
            "4" => Strings.Perf_BattDead,
            "5" => Strings.Perf_BattOverVoltage,
            "7" => Strings.Perf_BattCold,
            _ => Get("health"),
        };
        var plugged = Get("plugged") switch
        {
            "0" => Strings.Perf_BattNotPlugged,
            "1" => "AC",
            "2" => "USB",
            "4" => Strings.Perf_BattWireless,
            _ => Get("plugged"),
        };
        if (int.TryParse(Get("temperature"), out var t)) dict["temperature"] = $"{t / 10.0:F1} °C";

        BatteryText =
            string.Format(Strings.Perf_BattLine1Format, Get("level"), status, plugged) + "\n" +
            string.Format(Strings.Perf_BattLine2Format, health, dict.GetValueOrDefault("temperature", "—"), Get("voltage")) + "\n" +
            string.Format(Strings.Perf_BattLine3Format, Get("technology"), Get("charge counter"));
    }

    private async Task SetBatteryLevelAsync()
    {
        if (!int.TryParse(VirtualLevel, out var level)) return;
        await RunShell($"dumpsys battery set level {Math.Clamp(level, 1, 100)}");
        await RefreshBatteryAsync();
        Status = string.Format(Strings.Perf_BattSimulatedFormat, level);
    }

    private async Task BatteryResetAsync()
    {
        await RunShell("dumpsys battery reset");
        await RefreshBatteryAsync();
        Status = Strings.Perf_BattReset;
    }

    private async Task LoadMemInfoAsync()
    {
        var pkg = MemPackage.Trim();
        if (pkg.Length == 0) { Status = Strings.Perf_EnterPackage; return; }
        var r = await RunShell($"dumpsys meminfo {pkg}");
        MemInfoText = r.StdOut.Trim();
        Status = r.Success
            ? string.Format(Strings.Perf_MemLoadedFormat, pkg)
            : string.Format(Strings.Common_FailedFormat, r.Combined.Trim());
    }

    private async Task StartupTestAsync()
    {
        var comp = StartupComponent.Trim();
        if (comp.Length == 0) { Status = Strings.Perf_EnterComponent; return; }
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
                lines.AppendLine(string.Format(Strings.Perf_TryFormat, i + 1, t));
            }
            else
            {
                lines.AppendLine(string.Format(Strings.Perf_TryFailedFormat, i + 1,
                    r.Combined.Split('\n').FirstOrDefault()?.Trim()));
            }
            await Task.Delay(800);
        }
        if (times.Count > 0)
            lines.AppendLine(string.Format(Strings.Perf_TrySummaryFormat, times.Average(), times.Min(), times.Max()));
        StartupResult = lines.ToString();
        Status = Strings.Perf_StartupDone;
    }

    private async Task LoadGfxInfoAsync()
    {
        var pkg = GfxPackage.Trim();
        if (pkg.Length == 0) { Status = Strings.Perf_EnterPackage; return; }
        var r = await RunShell($"dumpsys gfxinfo {pkg}");
        var total = GfxTotalRegex().Match(r.StdOut);
        var janky = GfxJankyRegex().Match(r.StdOut);
        GfxResult = total.Success
            ? string.Format(Strings.Perf_GfxResultFormat, total.Groups[1].Value, janky.Groups[1].Value, janky.Groups[2].Value)
            : r.Combined.Trim();
        Status = Strings.Perf_GfxLoaded;
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
            CpuFreqs.Add(string.Format(Strings.Perf_CpuFreqFormat, name, FmtFreq(curList[i]),
                FmtFreq(maxList.Count > i ? maxList[i] : "")));
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
        Status = string.Format(Strings.Perf_DropBoxCountFormat, entries.Count, Math.Min(200, entries.Count));
    }

    private async Task ViewSelectedDropBoxAsync()
    {
        if (SelectedDropBox is null) { Status = Strings.Perf_SelectDropBoxFirst; return; }
        var r = await RunShell($"dumpsys dropbox --print {SelectedDropBox.Tag}");
        var text = r.StdOut.Trim();
        DropBoxText = text.Length > 20000 ? text[..20000] + Strings.More_TruncatedSuffix : text;
        Status = string.Format(Strings.Perf_DropBoxLoadedFormat, SelectedDropBox.Tag);
    }
}
