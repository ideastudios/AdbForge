using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using AdbToolkit.Models;
using AdbToolkit.Services;

namespace AdbToolkit.ViewModels;

/// <summary>系统调试页：Settings 浏览器、getprop、Intent 构造器、调试预设、SELinux/刷机、终端。</summary>
public class SysDebugViewModel : ObservableObject
{
    public SysDebugViewModel()
    {
        Namespaces = new List<string> { "system", "secure", "global" };

        SearchSettingsCommand = new RelayCommand(async () => await RunBusyAsync(SearchSettingsAsync));
        GetSettingCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
            SettingValue = (await RunShell($"settings get {SelectedNamespace} {SettingKey.Trim()}")).StdOut.Trim()));
        PutSettingCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell($"settings put {SelectedNamespace} {SettingKey.Trim()} {Quote(SettingValue.Trim())}");
            Status = r.Success ? $"已写入 {SettingKey} = {SettingValue}" : $"失败：{r.Combined.Trim()}";
        }));
        DeleteSettingCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell($"settings delete {SelectedNamespace} {SettingKey.Trim()}");
            Status = r.Success ? $"已删除 {SettingKey}" : $"失败：{r.Combined.Trim()}";
        }));

        RefreshPropsCommand = new RelayCommand(async () => await RunBusyAsync(LoadPropsAsync));

        RunIntentCommand = new RelayCommand(async () => await RunBusyAsync(RunIntentAsync));

        OpenSettingsCommand = new RelayCommand(async p =>
        {
            if (p is not string action || action.Length == 0) return;
            await RunBusyAsync(async () =>
            {
                var r = await RunShell($"am start -a {action}");
                Status = r.Success ? $"已打开：{action}" : $"打开失败：{r.Combined.Trim()}";
            });
        });
        OpenAppDetailsByPackageCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var pkg = SettingsPkgInput.Trim();
            if (pkg.Length == 0) { Status = "请输入包名"; return; }
            var r = await RunShell(
                $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{pkg}\"");
            Status = r.Success ? $"已打开 {pkg} 的应用详情" : $"打开失败：{r.Combined.Trim()}";
        }));

        ApplyPresetCommand = new RelayCommand(async p =>
        {
            if (p is PresetItem item) await RunBusyAsync(() => ApplyPresetAsync(item));
        });

        GetEnforceCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell("getenforce");
            EnforceState = r.StdOut.Trim();
            Status = $"SELinux 状态：{EnforceState}";
        }));
        SetEnforceCommand = new RelayCommand(async p =>
        {
            if (MessageBox.Show("修改 SELinux 状态可能带来安全风险，仅建议在调试机上使用。继续？",
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var permissive = p as string == "0";
            var r = await RunShell($"setenforce {(permissive ? 0 : 1)}");
            EnforceState = r.Success ? (permissive ? "Permissive" : "Enforcing") : "未知";
            Status = r.Success ? $"已切换 SELinux：{EnforceState}" : $"失败（可能需要 root）：{r.Combined.Trim()}";
        });

        PersistAdbCommand = new RelayCommand(async () => await RunBusyAsync(PersistAdbAsync));
        ListPstoreCommand = new RelayCommand(async () => await RunBusyAsync(ListPstoreAsync));
        PullPstoreCommand = new RelayCommand(async () => await RunBusyAsync(PullPstoreAsync));
        ReadPropFileCommand = new RelayCommand(async () => await RunBusyAsync(ReadPropFileAsync));
        SavePropFileCommand = new RelayCommand(async () => await RunBusyAsync(SavePropFileAsync));

        RunTerminalCommand = new RelayCommand(async () => await RunBusyAsync(RunTerminalAsync));
    }

    public record PresetItem(string Label, string Description, string[] Commands);

    /// <summary>系统设置快捷入口（am start -a android.settings.*）。</summary>
    public record SettingsEntry(string Label, string Action);

    public List<string> Namespaces { get; }

    // ---------- Settings 浏览器 ----------
    public ObservableCollection<KeyValuePair<string, string>> SettingsItems { get; } = new();
    private string _selectedNamespace = "global";
    public string SelectedNamespace { get => _selectedNamespace; set => SetProperty(ref _selectedNamespace, value); }
    private string _settingsSearch = "";
    public string SettingsSearch { get => _settingsSearch; set => SetProperty(ref _settingsSearch, value); }
    private string _settingKey = "";
    public string SettingKey { get => _settingKey; set => SetProperty(ref _settingKey, value); }
    private string _settingValue = "";
    public string SettingValue { get => _settingValue; set => SetProperty(ref _settingValue, value); }

    // ---------- getprop ----------
    public ObservableCollection<KeyValuePair<string, string>> Props { get; } = new();
    private List<KeyValuePair<string, string>> _allProps = new();

    /// <summary>搜索建议（属性名补全提示，最多 8 条）。</summary>
    public ObservableCollection<string> PropSuggestions { get; } = new();

    private bool _propSuggestionsOpen;
    public bool PropSuggestionsOpen
    {
        get => _propSuggestionsOpen;
        set => SetProperty(ref _propSuggestionsOpen, value);
    }

    private string _propSearch = "";
    public string PropSearch
    {
        get => _propSearch;
        set
        {
            if (!SetProperty(ref _propSearch, value)) return;
            ApplyPropFilter();
            UpdatePropSuggestions();
        }
    }

    private void ApplyPropFilter()
    {
        Props.Clear();
        foreach (var p in FilterPairs(_allProps, _propSearch)) Props.Add(p);
    }

    /// <summary>根据输入生成属性名提示：前缀匹配优先，其次包含匹配。</summary>
    private void UpdatePropSuggestions()
    {
        PropSuggestions.Clear();
        var kw = _propSearch.Trim();
        if (kw.Length == 0 || _allProps.Count == 0)
        {
            PropSuggestionsOpen = false;
            return;
        }

        var matches = _allProps
            .Where(p => p.Key.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
            .Concat(_allProps.Where(p => !p.Key.StartsWith(kw, StringComparison.OrdinalIgnoreCase)
                                         && p.Key.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Key)
            .Take(8)
            .ToList();

        foreach (var m in matches) PropSuggestions.Add(m);
        PropSuggestionsOpen = matches.Count > 0;
    }

    /// <summary>采纳一条提示：按该属性名精确过滤并关闭弹层。</summary>
    public void PickPropSuggestion(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _propSearch = key;
        OnPropertyChanged(nameof(PropSearch));
        ApplyPropFilter();
        PropSuggestionsOpen = false;
        Status = $"已按 {key} 过滤";
    }

    /// <summary>当前在属性列表中选中的条目（右键修改/复制用）。</summary>
    private KeyValuePair<string, string>? _selectedProp;
    public KeyValuePair<string, string>? SelectedProp
    {
        get => _selectedProp;
        set => SetProperty(ref _selectedProp, value);
    }

    /// <summary>右键「修改值...」：弹窗输入新值 → setprop → 回读校验并更新列表。</summary>
    public async Task EditSelectedPropAsync()
    {
        var sel = SelectedProp;
        if (sel is not { } prop || prop.Key.Length == 0)
        {
            Status = "请先选中一个属性";
            return;
        }

        if (prop.Key.StartsWith("ro.", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("ro. 开头的属性是只读的，无法通过 setprop 修改。",
                "只读属性", MessageBoxButton.OK, MessageBoxImage.Information);
            Status = $"{prop.Key} 为只读属性（ro. 前缀），无法修改";
            return;
        }

        var dlg = new Views.PropEditWindow(prop.Key, prop.Value)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dlg.ShowDialog() != true) return;
        var newVal = dlg.PropValue;

        // 空值需要显式空串参数；含空格要加引号
        var valueArg = newVal.Length == 0 ? "\"\""
            : newVal.Contains(' ') ? $"\"{newVal}\""
            : newVal;

        var r = await RunShell($"setprop {prop.Key} {valueArg}");
        if (!r.Success)
        {
            Status = $"setprop 失败（可能需要 root）：{r.Combined.Trim()}";
            return;
        }

        // 回读实际值（setprop 对只读属性可能静默失败）
        var back = await RunShell($"getprop {prop.Key}");
        var actual = back.StdOut.Trim();
        for (var i = 0; i < _allProps.Count; i++)
        {
            if (_allProps[i].Key == prop.Key)
            {
                _allProps[i] = new KeyValuePair<string, string>(prop.Key, actual);
                break;
            }
        }
        ApplyPropFilter();

        Status = actual == newVal
            ? $"已修改 {prop.Key} = {actual}"
            : $"已执行 setprop，但设备回读为 {prop.Key} = {actual}（可能未生效）";
    }

    /// <summary>右键复制属性名或属性值。</summary>
    public void CopyProp(bool copyKey)
    {
        var sel = SelectedProp;
        if (sel is not { } prop || prop.Key.Length == 0)
        {
            Status = "请先选中一个属性";
            return;
        }
        try
        {
            Clipboard.SetText(copyKey ? prop.Key : prop.Value);
            Status = $"已复制：{(copyKey ? prop.Key : prop.Value)}";
        }
        catch { /* 剪贴板被占用等极端情况 */ }
    }

    // ---------- Intent 构造 ----------
    private string _intentAction = "";
    public string IntentAction { get => _intentAction; set => SetProperty(ref _intentAction, value); }
    private string _intentUri = "";
    public string IntentUri { get => _intentUri; set => SetProperty(ref _intentUri, value); }
    private string _intentComponent = "";
    public string IntentComponent { get => _intentComponent; set => SetProperty(ref _intentComponent, value); }
    private string _intentExtras = "";
    public string IntentExtras { get => _intentExtras; set => SetProperty(ref _intentExtras, value); }
    private string _intentOutput = "";
    public string IntentOutput { get => _intentOutput; private set => SetProperty(ref _intentOutput, value); }

    // ---------- 设置页面快捷入口 ----------
    public List<SettingsEntry> SettingsGeneral { get; } = new()
    {
        new("设置主页",       "android.settings.SETTINGS"),
        new("系统更新",       "android.settings.SYSTEM_UPDATE_SETTINGS"),
        new("关于手机",       "android.settings.DEVICE_INFO_SETTINGS"),
        new("设备名称",       "android.settings.DEVICE_NAME"),
        new("开发者选项",     "android.settings.APPLICATION_DEVELOPMENT_SETTINGS"),
        new("备份与重置",     "android.settings.PRIVACY_SETTINGS"),
        new("多用户",         "android.settings.USER_SETTINGS"),
        new("存储空间",       "android.settings.INTERNAL_STORAGE_SETTINGS"),
        new("电池",           "android.settings.BATTERY_SAVER_SETTINGS"),
        new("电池优化",       "android.settings.IGNORE_BATTERY_OPTIMIZATION_SETTINGS"),
    };

    public List<SettingsEntry> SettingsApps { get; } = new()
    {
        new("应用管理",         "android.settings.APPLICATION_SETTINGS"),
        new("全部应用（运行中）", "android.settings.MANAGE_ALL_APPLICATIONS_SETTINGS"),
        new("默认应用",         "android.settings.MANAGE_DEFAULT_APPS_SETTINGS"),
        new("权限管理器",       "android.settings.MANAGE_PERMISSIONS"),
        new("特殊应用访问",     "android.settings.SPECIAL_ACCESS_SETTINGS"),
        new("使用情况访问",     "android.settings.USAGE_ACCESS_SETTINGS"),
        new("通知使用权",       "android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS"),
        new("未知来源安装",     "android.settings.MANAGE_UNKNOWN_APP_SOURCES"),
        new("WebView 实现",     "android.settings.WEBVIEW_SETTINGS"),
        new("画中画",           "android.settings.PICTURE_IN_PICTURE_SETTINGS"),
    };

    public List<SettingsEntry> SettingsNetwork { get; } = new()
    {
        new("WLAN",         "android.settings.WIFI_SETTINGS"),
        new("WLAN 高级",    "android.settings.WIFI_IP_SETTINGS"),
        new("蓝牙",         "android.settings.BLUETOOTH_SETTINGS"),
        new("位置信息",     "android.settings.LOCATION_SOURCE_SETTINGS"),
        new("飞行模式",     "android.settings.AIRPLANE_MODE_SETTINGS"),
        new("流量使用",     "android.settings.DATA_USAGE_SETTINGS"),
        new("移动网络",     "android.settings.NETWORK_OPERATOR_SETTINGS"),
        new("数据漫游",     "android.settings.ACTION_DATA_ROAMING_SETTINGS"),
        new("APN 接入点",   "android.settings.APN_SETTINGS"),
        new("热点与共享",   "android.settings.TETHER_SETTINGS"),
        new("VPN",          "android.settings.VPN_SETTINGS"),
        new("NFC",          "android.settings.NFC_SETTINGS"),
        new("投屏",         "android.settings.CAST_SETTINGS"),
        new("SIM 卡",       "android.settings.SIM_CARD_SETTINGS"),
    };

    public List<SettingsEntry> SettingsDevice { get; } = new()
    {
        new("显示",           "android.settings.DISPLAY_SETTINGS"),
        new("夜间模式",       "android.settings.NIGHT_DISPLAY_SETTINGS"),
        new("声音",           "android.settings.SOUND_SETTINGS"),
        new("免打扰",         "android.settings.ZEN_MODE_SETTINGS"),
        new("日期和时间",     "android.settings.DATE_SETTINGS"),
        new("语言区域",       "android.settings.LOCALE_SETTINGS"),
        new("语言和输入法",   "android.settings.INPUT_METHOD_SETTINGS"),
        new("实体键盘",       "android.settings.HARD_KEYBOARD_SETTINGS"),
        new("用户词典",       "android.settings.USER_DICTIONARY_SETTINGS"),
        new("语音输入",       "android.settings.VOICE_INPUT_SETTINGS"),
        new("安全",           "android.settings.SECURITY_SETTINGS"),
        new("受信任的凭据",   "android.settings.TRUSTED_CREDENTIALS"),
        new("辅助功能",       "android.settings.ACCESSIBILITY_SETTINGS"),
        new("字幕",           "android.settings.CAPTIONING_SETTINGS"),
        new("屏保",           "android.settings.DREAM_SETTINGS"),
        new("壁纸",           "android.settings.WALLPAPER_SETTINGS"),
        new("底座",           "android.settings.DOCK_SETTINGS"),
        new("主屏幕",         "android.settings.HOME_SETTINGS"),
        new("搜索助手",       "android.settings.SEARCH_SETTINGS"),
        new("账号与同步",     "android.settings.SYNC_SETTINGS"),
        new("添加账号",       "android.settings.ADD_ACCOUNT_SETTINGS"),
        new("恢复出厂",       "android.settings.FACTORY_RESET_SETTINGS"),
    };

    private string _settingsPkgInput = "";
    public string SettingsPkgInput
    {
        get => _settingsPkgInput;
        set => SetProperty(ref _settingsPkgInput, value);
    }

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenAppDetailsByPackageCommand { get; }

    // ---------- 预设 ----------
    public List<PresetItem> Presets { get; } = new()
    {
        new("动画归零", "关闭三类动画，提速调试", new[]
        {
            "settings put global window_animation_scale 0",
            "settings put global transition_animation_scale 0",
            "settings put global animator_duration_scale 0",
        }),
        new("动画恢复", "恢复三类动画为 1", new[]
        {
            "settings put global window_animation_scale 1",
            "settings put global transition_animation_scale 1",
            "settings put global animator_duration_scale 1",
        }),
        new("充电保持唤醒", "插电时不锁屏休眠", new[]
            { "settings put global stay_on_while_plugged_in 7" }),
        new("取消保持唤醒", "恢复默认休眠策略", new[]
            { "settings put global stay_on_while_plugged_in 0" }),
        new("自由窗口", "启用自由窗口多窗口 + 强制 Activity 可调大小", new[]
        {
            "settings put global enable_freeform_support 1",
            "settings put global force_resizable_activities 1",
        }),
        new("NTP 服务器", "切换 NTP 为国内源（例）", new[]
            { "settings put global ntp_server cn.ntp.org.cn" }),
        new("横屏", "强制横屏（user_rotation=1）", new[]
            { "content insert --uri content://settings/system --bind name:s:user_rotation --bind value:i:1" }),
        new("竖屏", "强制竖屏（user_rotation=0）", new[]
            { "content insert --uri content://settings/system --bind name:s:user_rotation --bind value:i:0" }),
        new("熄屏时钟常亮", "锁屏上显示时钟/通知", new[]
            { "settings put secure doze_always_on 1" }),
    };

    // ---------- SELinux / 刷机 ----------
    private string _enforceState = "";
    public string EnforceState { get => _enforceState; set => SetProperty(ref _enforceState, value); }
    private string _persistPort = "5555";
    public string PersistPort { get => _persistPort; set => SetProperty(ref _persistPort, value); }
    private string _pstoreList = "";
    public string PstoreList { get => _pstoreList; private set => SetProperty(ref _pstoreList, value); }

    // ---------- build.prop ----------
    public List<string> PropFilePaths { get; } = new()
    {
        "/system/build.prop",
        "/vendor/build.prop",
        "/system/etc/prop.default",
    };

    private string _propFilePath = "/system/build.prop";
    public string PropFilePath { get => _propFilePath; set => SetProperty(ref _propFilePath, value); }
    private string _propFileContent = "";
    public string PropFileContent { get => _propFileContent; set => SetProperty(ref _propFileContent, value); }

    // ---------- 终端 ----------
    private string _terminalCommand = "";
    public string TerminalCommand { get => _terminalCommand; set => SetProperty(ref _terminalCommand, value); }
    public ObservableCollection<string> TerminalHistory { get; } = new();
    private string _terminalOutput = "";
    public string TerminalOutput { get => _terminalOutput; private set => SetProperty(ref _terminalOutput, value); }

    private bool _busy;
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    private string _status = "就绪";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public RelayCommand SearchSettingsCommand { get; }
    public RelayCommand GetSettingCommand { get; }
    public RelayCommand PutSettingCommand { get; }
    public RelayCommand DeleteSettingCommand { get; }
    public RelayCommand RefreshPropsCommand { get; }
    public RelayCommand RunIntentCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }
    public RelayCommand GetEnforceCommand { get; }
    public RelayCommand SetEnforceCommand { get; }
    public RelayCommand PersistAdbCommand { get; }
    public RelayCommand ListPstoreCommand { get; }
    public RelayCommand PullPstoreCommand { get; }
    public RelayCommand ReadPropFileCommand { get; }
    public RelayCommand SavePropFileCommand { get; }
    public RelayCommand RunTerminalCommand { get; }

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

    private static string Quote(string s) =>
        string.IsNullOrWhiteSpace(s) || !s.Contains(' ') ? s : $"\"{s}\"";

    private static List<KeyValuePair<string, string>> FilterPairs(
        List<KeyValuePair<string, string>> source, string keyword)
    {
        if (keyword.Length == 0) return source;
        return source.Where(p =>
            p.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            p.Value.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ---------- Settings ----------

    public async Task LoadOnNavigateAsync()
    {
        if (_allProps.Count > 0) return;
        await RunBusyAsync(async () =>
        {
            await LoadPropsAsync();
            await SearchSettingsAsync();
            var r = await RunShell("getenforce");
            EnforceState = r.StdOut.Trim();
        });
    }

    private async Task SearchSettingsAsync()
    {
        var items = await AppSession.Adb.ListSettingsAsync(DeviceStore.SelectedSerial!, SelectedNamespace);
        SettingsItems.Clear();
        foreach (var p in FilterPairs(items, SettingsSearch)) SettingsItems.Add(p);
        Status = $"{SelectedNamespace} 共 {items.Count} 项，筛选后 {SettingsItems.Count} 项";
    }

    // ---------- getprop ----------

    /// <summary>从设备重新拉取全部属性，并清空搜索框显示所有条目。</summary>
    private async Task LoadPropsAsync()
    {
        _allProps = await AppSession.Adb.ListPropsAsync(DeviceStore.SelectedSerial!);
        _propSearch = "";
        OnPropertyChanged(nameof(PropSearch));
        ApplyPropFilter();
        PropSuggestionsOpen = false;
        Status = $"getprop 共 {_allProps.Count} 项";
    }

    // ---------- Intent ----------

    private async Task RunIntentAsync()
    {
        var sb = new StringBuilder("am start");
        if (IntentAction.Trim().Length > 0) sb.Append($" -a {IntentAction.Trim()}");
        if (IntentUri.Trim().Length > 0) sb.Append($" -d {Quote(IntentUri.Trim())}");
        if (IntentComponent.Trim().Length > 0) sb.Append($" -n {IntentComponent.Trim()}");
        foreach (var raw in IntentExtras.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var typeAndKey = line[..idx];
            var value = line[(idx + 1)..];
            var colon = typeAndKey.IndexOf(':');
            if (colon <= 0) continue;
            var type = typeAndKey[..colon].Trim();
            var key = typeAndKey[(colon + 1)..].Trim();
            switch (type)
            {
                case "es": sb.Append($" --es {key} {Quote(value)}"); break;
                case "ei": sb.Append($" --ei {key} {value}"); break;
                case "ez": sb.Append($" --ez {key} {value}"); break;
                case "eb": sb.Append($" --eb {key} {value}"); break;
                case "el": sb.Append($" --el {key} {value}"); break;
                default: continue;
            }
        }

        var r = await RunShell(sb.ToString());
        IntentOutput = (r.StdOut + r.StdErr).Trim();
        Status = r.Success ? "Intent 已发送" : "Intent 发送失败";
    }

    // ---------- 预设 ----------

    private async Task ApplyPresetAsync(PresetItem item)
    {
        var results = new StringBuilder();
        foreach (var cmd in item.Commands)
        {
            var r = await RunShell(cmd);
            results.AppendLine(r.Success
                ? $"[OK] {cmd}"
                : $"[失败] {cmd} → {r.Combined.Trim()}");
        }
        TerminalOutput = results.ToString();
        Status = $"预设「{item.Label}」已执行";
    }

    // ---------- SELinux / ADB 持久化 / pstore / build.prop ----------

    private async Task PersistAdbAsync()
    {
        if (MessageBox.Show("将以 root 重启 adbd 使 WiFi 调试端口持久化（需要已 root 的调试机）。继续？",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var port = PersistPort.Trim();
        var r1 = await RunShell($"setprop service.adb.tcp.port {port}");
        var r2 = await RunShell("stop adbd");
        var r3 = await RunShell("start adbd");
        Status = r1.Success && r2.Success && r3.Success
            ? $"已持久化 TCP {port}（重启设备后仍生效，需重新 adb connect）"
            : $"执行失败（需要 root）：{(r1.Combined + r2.Combined + r3.Combined).Trim()}";
    }

    private async Task ListPstoreAsync()
    {
        var r = await RunShell("ls -la /sys/fs/pstore/ 2>/dev/null");
        PstoreList = r.StdOut.Trim().Length == 0
            ? "（空）没有 pstore 文件，或需要 root 才能查看"
            : r.Combined.Trim();
        Status = "pstore 已刷新";
    }

    private async Task PullPstoreAsync()
    {
        var serial = DeviceStore.SelectedSerial!;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit_pstore");
        Directory.CreateDirectory(dir);
        var ls = await AppSession.Adb.RunShellAsync("ls /sys/fs/pstore/", serial);
        var names = ls.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        if (names.Count == 0) { Status = "没有可拉取的 pstore 文件（可能需要 root）"; return; }

        var ok = 0;
        foreach (var name in names)
        {
            var r = await AppSession.Adb.PullAsync(serial, $"/sys/fs/pstore/{name}", dir);
            if (r.Success) ok++;
        }
        PstoreList += $"\n\n已拉取 {ok}/{names.Count} 个文件到 {dir}";
        Status = $"内核恐慌日志已拉取到 {dir}";
    }

    private async Task ReadPropFileAsync()
    {
        var r = await RunShell($"cat {PropFilePath.Trim()}");
        if (!r.Success && r.StdOut.Trim().Length == 0)
        {
            Status = $"读取失败：{r.Combined.Trim()}";
            return;
        }
        PropFileContent = r.StdOut;
        Status = $"已读取 {PropFilePath}（{PropFileContent.Length:N0} 字符）";
    }

    private async Task SavePropFileAsync()
    {
        if (MessageBox.Show("修改 build.prop 可能导致设备无法开机，仅建议在调试机/root 机上操作。\n" +
                "将执行：adb root → remount → 设备端备份 .bak → push 覆盖。继续？",
                "高危操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var serial = DeviceStore.SelectedSerial!;
        var path = PropFilePath.Trim();
        await AppSession.Adb.RunAsync("root", serial);
        await Task.Delay(2000);
        await AppSession.Adb.RunAsync("remount", serial);

        var tmp = Path.Combine(Path.GetTempPath(), "adbtoolkit_build.prop");
        await File.WriteAllTextAsync(tmp, PropFileContent.ReplaceLineEndings("\n"));
        var backup = await AppSession.Adb.RunShellAsync($"cp {path} {path}.bak 2>/dev/null", serial);

        var push = await AppSession.Adb.PushAsync(serial, tmp, path);
        Status = push.Success
            ? $"已写入 {path}（设备端备份：{path}.bak {(backup.Success ? "OK" : "失败")}）。建议重启设备验证。"
            : $"写入失败（需要 root + remount）：{push.Combined.Trim()}";
    }

    // ---------- 终端 ----------

    private async Task RunTerminalAsync()
    {
        var cmd = TerminalCommand.Trim();
        if (cmd.Length == 0) return;
        var r = await RunShell(cmd);
        TerminalOutput = $"$ {cmd}\n{r.Combined.Trim()}\n\n{TerminalOutput}";
        TerminalHistory.Insert(0, cmd);
        while (TerminalHistory.Count > 50) TerminalHistory.RemoveAt(TerminalHistory.Count - 1);
        Status = r.Success ? "命令执行完成" : $"命令退出码 {r.ExitCode}";
    }
}
