using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using AdbToolkit.Models;
using AdbToolkit.Resources;
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
            Status = r.Success
                ? string.Format(Strings.SysDbg_SettingWrittenFormat, SettingKey, SettingValue)
                : string.Format(Strings.Common_FailedFormat, r.Combined.Trim());
        }));
        DeleteSettingCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell($"settings delete {SelectedNamespace} {SettingKey.Trim()}");
            Status = r.Success
                ? string.Format(Strings.SysDbg_SettingDeletedFormat, SettingKey)
                : string.Format(Strings.Common_FailedFormat, r.Combined.Trim());
        }));

        RefreshPropsCommand = new RelayCommand(async () => await RunBusyAsync(LoadPropsAsync));

        RunIntentCommand = new RelayCommand(async () => await RunBusyAsync(RunIntentAsync));

        OpenSettingsCommand = new RelayCommand(async p =>
        {
            if (p is not string action || action.Length == 0) return;
            await RunBusyAsync(async () =>
            {
                var r = await RunShell($"am start -a {action}");
                Status = r.Success
                    ? string.Format(Strings.SysDbg_OpenedFormat, action)
                    : string.Format(Strings.Common_OpenFailedFormat, r.Combined.Trim());
            });
        });
        OpenAppDetailsByPackageCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var pkg = SettingsPkgInput.Trim();
            if (pkg.Length == 0) { Status = Strings.SysDbg_EnterPackage; return; }
            var r = await RunShell(
                $"am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d \"package:{pkg}\"");
            Status = r.Success
                ? string.Format(Strings.Main_OpenedAppDetailsFormat, pkg)
                : string.Format(Strings.Common_OpenFailedFormat, r.Combined.Trim());
        }));

        ApplyPresetCommand = new RelayCommand(async p =>
        {
            if (p is PresetItem item) await RunBusyAsync(() => ApplyPresetAsync(item));
        });

        GetEnforceCommand = new RelayCommand(async () => await RunBusyAsync(async () =>
        {
            var r = await RunShell("getenforce");
            EnforceState = r.StdOut.Trim();
            Status = string.Format(Strings.SysDbg_SelinuxStateFormat, EnforceState);
        }));
        SetEnforceCommand = new RelayCommand(async p =>
        {
            if (MessageBox.Show(Strings.SysDbg_SelinuxConfirm,
                    Strings.SysDbg_ConfirmCaption, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var permissive = p as string == "0";
            var r = await RunShell($"setenforce {(permissive ? 0 : 1)}");
            EnforceState = r.Success ? (permissive ? "Permissive" : "Enforcing") : Strings.SysDbg_UnknownState;
            Status = r.Success
                ? string.Format(Strings.SysDbg_SelinuxSwitchedFormat, EnforceState)
                : string.Format(Strings.SysDbg_FailedRootFormat, r.Combined.Trim());
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
        Status = string.Format(Strings.SysDbg_FilteredByFormat, key);
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
            Status = Strings.SysDbg_SelectPropFirst;
            return;
        }

        if (prop.Key.StartsWith("ro.", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Strings.SysDbg_ReadOnlyPropText,
                Strings.SysDbg_ReadOnlyPropCaption, MessageBoxButton.OK, MessageBoxImage.Information);
            Status = string.Format(Strings.SysDbg_ReadOnlyStatusFormat, prop.Key);
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
            Status = string.Format(Strings.SysDbg_SetpropFailedFormat, r.Combined.Trim());
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
            ? string.Format(Strings.SysDbg_PropModifiedFormat, prop.Key, actual)
            : string.Format(Strings.SysDbg_PropNotAppliedFormat, prop.Key, actual);
    }

    /// <summary>右键复制属性名或属性值。</summary>
    public void CopyProp(bool copyKey)
    {
        var sel = SelectedProp;
        if (sel is not { } prop || prop.Key.Length == 0)
        {
            Status = Strings.SysDbg_SelectPropFirst;
            return;
        }
        try
        {
            Clipboard.SetText(copyKey ? prop.Key : prop.Value);
            Status = string.Format(Strings.Common_CopiedFormat, copyKey ? prop.Key : prop.Value);
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
        new(Strings.SysDbg_Set_SettingsHome, "android.settings.SETTINGS"),
        new(Strings.SysDbg_Set_SystemUpdate, "android.settings.SYSTEM_UPDATE_SETTINGS"),
        new(Strings.SysDbg_Set_AboutPhone, "android.settings.DEVICE_INFO_SETTINGS"),
        new(Strings.SysDbg_Set_DeviceName, "android.settings.DEVICE_NAME"),
        new(Strings.SysDbg_Set_DevOptions, "android.settings.APPLICATION_DEVELOPMENT_SETTINGS"),
        new(Strings.SysDbg_Set_BackupReset, "android.settings.PRIVACY_SETTINGS"),
        new(Strings.SysDbg_Set_MultiUser, "android.settings.USER_SETTINGS"),
        new(Strings.SysDbg_Set_Storage, "android.settings.INTERNAL_STORAGE_SETTINGS"),
        new(Strings.SysDbg_Set_Battery, "android.settings.BATTERY_SAVER_SETTINGS"),
        new(Strings.SysDbg_Set_BatteryOpt, "android.settings.IGNORE_BATTERY_OPTIMIZATION_SETTINGS"),
    };

    public List<SettingsEntry> SettingsApps { get; } = new()
    {
        new(Strings.SysDbg_Set_AppManager, "android.settings.APPLICATION_SETTINGS"),
        new(Strings.SysDbg_Set_AllAppsRunning, "android.settings.MANAGE_ALL_APPLICATIONS_SETTINGS"),
        new(Strings.SysDbg_Set_DefaultApps, "android.settings.MANAGE_DEFAULT_APPS_SETTINGS"),
        new(Strings.SysDbg_Set_PermissionManager, "android.settings.MANAGE_PERMISSIONS"),
        new(Strings.SysDbg_Set_SpecialAccess, "android.settings.SPECIAL_ACCESS_SETTINGS"),
        new(Strings.SysDbg_Set_UsageAccess, "android.settings.USAGE_ACCESS_SETTINGS"),
        new(Strings.SysDbg_Set_NotificationAccess, "android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS"),
        new(Strings.SysDbg_Set_UnknownSources, "android.settings.MANAGE_UNKNOWN_APP_SOURCES"),
        new(Strings.SysDbg_Set_WebView, "android.settings.WEBVIEW_SETTINGS"),
        new(Strings.SysDbg_Set_Pip, "android.settings.PICTURE_IN_PICTURE_SETTINGS"),
    };

    public List<SettingsEntry> SettingsNetwork { get; } = new()
    {
        new("WLAN", "android.settings.WIFI_SETTINGS"),
        new(Strings.SysDbg_Set_WlanAdvanced, "android.settings.WIFI_IP_SETTINGS"),
        new(Strings.SysDbg_Set_Bluetooth, "android.settings.BLUETOOTH_SETTINGS"),
        new(Strings.SysDbg_Set_Location, "android.settings.LOCATION_SOURCE_SETTINGS"),
        new(Strings.SysDbg_Set_AirplaneMode, "android.settings.AIRPLANE_MODE_SETTINGS"),
        new(Strings.SysDbg_Set_DataUsage, "android.settings.DATA_USAGE_SETTINGS"),
        new(Strings.SysDbg_Set_MobileNetwork, "android.settings.NETWORK_OPERATOR_SETTINGS"),
        new(Strings.SysDbg_Set_DataRoaming, "android.settings.ACTION_DATA_ROAMING_SETTINGS"),
        new(Strings.SysDbg_Set_Apn, "android.settings.APN_SETTINGS"),
        new(Strings.SysDbg_Set_Hotspot, "android.settings.TETHER_SETTINGS"),
        new("VPN", "android.settings.VPN_SETTINGS"),
        new("NFC", "android.settings.NFC_SETTINGS"),
        new(Strings.SysDbg_Set_Cast, "android.settings.CAST_SETTINGS"),
        new(Strings.SysDbg_Set_SimCard, "android.settings.SIM_CARD_SETTINGS"),
    };

    public List<SettingsEntry> SettingsDevice { get; } = new()
    {
        new(Strings.SysDbg_Set_Display, "android.settings.DISPLAY_SETTINGS"),
        new(Strings.SysDbg_Set_NightMode, "android.settings.NIGHT_DISPLAY_SETTINGS"),
        new(Strings.SysDbg_Set_Sound, "android.settings.SOUND_SETTINGS"),
        new(Strings.SysDbg_Set_DoNotDisturb, "android.settings.ZEN_MODE_SETTINGS"),
        new(Strings.SysDbg_Set_DateTime, "android.settings.DATE_SETTINGS"),
        new(Strings.SysDbg_Set_Locale, "android.settings.LOCALE_SETTINGS"),
        new(Strings.SysDbg_Set_LangInput, "android.settings.INPUT_METHOD_SETTINGS"),
        new(Strings.SysDbg_Set_PhysicalKeyboard, "android.settings.HARD_KEYBOARD_SETTINGS"),
        new(Strings.SysDbg_Set_UserDictionary, "android.settings.USER_DICTIONARY_SETTINGS"),
        new(Strings.SysDbg_Set_VoiceInput, "android.settings.VOICE_INPUT_SETTINGS"),
        new(Strings.SysDbg_Set_Security, "android.settings.SECURITY_SETTINGS"),
        new(Strings.SysDbg_Set_TrustedCerts, "android.settings.TRUSTED_CREDENTIALS"),
        new(Strings.SysDbg_Set_Accessibility, "android.settings.ACCESSIBILITY_SETTINGS"),
        new(Strings.SysDbg_Set_Captioning, "android.settings.CAPTIONING_SETTINGS"),
        new(Strings.SysDbg_Set_ScreenSaver, "android.settings.DREAM_SETTINGS"),
        new(Strings.SysDbg_Set_Wallpaper, "android.settings.WALLPAPER_SETTINGS"),
        new(Strings.SysDbg_Set_Dock, "android.settings.DOCK_SETTINGS"),
        new(Strings.SysDbg_Set_Home, "android.settings.HOME_SETTINGS"),
        new(Strings.SysDbg_Set_SearchAssistant, "android.settings.SEARCH_SETTINGS"),
        new(Strings.SysDbg_Set_AccountsSync, "android.settings.SYNC_SETTINGS"),
        new(Strings.SysDbg_Set_AddAccount, "android.settings.ADD_ACCOUNT_SETTINGS"),
        new(Strings.SysDbg_Set_FactoryReset, "android.settings.FACTORY_RESET_SETTINGS"),
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
        new(Strings.SysDbg_PresetAnimZeroLabel, Strings.SysDbg_PresetAnimZeroDesc, new[]
        {
            "settings put global window_animation_scale 0",
            "settings put global transition_animation_scale 0",
            "settings put global animator_duration_scale 0",
        }),
        new(Strings.SysDbg_PresetAnimRestoreLabel, Strings.SysDbg_PresetAnimRestoreDesc, new[]
        {
            "settings put global window_animation_scale 1",
            "settings put global transition_animation_scale 1",
            "settings put global animator_duration_scale 1",
        }),
        new(Strings.SysDbg_PresetStayAwakeOnLabel, Strings.SysDbg_PresetStayAwakeOnDesc, new[]
            { "settings put global stay_on_while_plugged_in 7" }),
        new(Strings.SysDbg_PresetStayAwakeOffLabel, Strings.SysDbg_PresetStayAwakeOffDesc, new[]
            { "settings put global stay_on_while_plugged_in 0" }),
        new(Strings.SysDbg_PresetFreeformLabel, Strings.SysDbg_PresetFreeformDesc, new[]
        {
            "settings put global enable_freeform_support 1",
            "settings put global force_resizable_activities 1",
        }),
        new(Strings.SysDbg_PresetNtpLabel, Strings.SysDbg_PresetNtpDesc, new[]
            { "settings put global ntp_server cn.ntp.org.cn" }),
        new(Strings.SysDbg_PresetLandscapeLabel, Strings.SysDbg_PresetLandscapeDesc, new[]
            { "content insert --uri content://settings/system --bind name:s:user_rotation --bind value:i:1" }),
        new(Strings.SysDbg_PresetPortraitLabel, Strings.SysDbg_PresetPortraitDesc, new[]
            { "content insert --uri content://settings/system --bind name:s:user_rotation --bind value:i:0" }),
        new(Strings.SysDbg_PresetDozeClockLabel, Strings.SysDbg_PresetDozeClockDesc, new[]
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
    private string _status = Strings.More_Ready;
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
        catch (Exception ex) { Status = string.Format(Strings.Common_ErrorFormat, ex.Message); }
        finally { IsBusy = false; }
    }

    private async Task<AdbResult> RunShell(string cmd)
    {
        var serial = DeviceStore.SelectedSerial ?? throw new InvalidOperationException(Strings.Common_SelectDeviceFirst);
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
        Status = string.Format(Strings.SysDbg_SettingsCountFormat, SelectedNamespace, items.Count, SettingsItems.Count);
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
        Status = string.Format(Strings.SysDbg_PropCountFormat, _allProps.Count);
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
        Status = r.Success ? Strings.SysDbg_IntentSent : Strings.SysDbg_IntentFailed;
    }

    // ---------- 预设 ----------

    private async Task ApplyPresetAsync(PresetItem item)
    {
        var results = new StringBuilder();
        foreach (var cmd in item.Commands)
        {
            var r = await RunShell(cmd);
            results.AppendLine(r.Success
                ? string.Format(Strings.SysDbg_ResultOkFormat, cmd)
                : string.Format(Strings.SysDbg_ResultFailFormat, cmd, r.Combined.Trim()));
        }
        TerminalOutput = results.ToString();
        Status = string.Format(Strings.SysDbg_PresetDoneFormat, item.Label);
    }

    // ---------- SELinux / ADB 持久化 / pstore / build.prop ----------

    private async Task PersistAdbAsync()
    {
        if (MessageBox.Show(Strings.SysDbg_PersistAdbConfirm,
                Strings.SysDbg_ConfirmCaption, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var port = PersistPort.Trim();
        var r1 = await RunShell($"setprop service.adb.tcp.port {port}");
        var r2 = await RunShell("stop adbd");
        var r3 = await RunShell("start adbd");
        Status = r1.Success && r2.Success && r3.Success
            ? string.Format(Strings.SysDbg_PersistAdbDoneFormat, port)
            : string.Format(Strings.SysDbg_FailedRootFormat, (r1.Combined + r2.Combined + r3.Combined).Trim());
    }

    private async Task ListPstoreAsync()
    {
        var r = await RunShell("ls -la /sys/fs/pstore/ 2>/dev/null");
        PstoreList = r.StdOut.Trim().Length == 0
            ? Strings.SysDbg_PstoreEmpty
            : r.Combined.Trim();
        Status = Strings.SysDbg_PstoreRefreshed;
    }

    private async Task PullPstoreAsync()
    {
        var serial = DeviceStore.SelectedSerial!;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AdbToolkit_pstore");
        Directory.CreateDirectory(dir);
        var ls = await AppSession.Adb.RunShellAsync("ls /sys/fs/pstore/", serial);
        var names = ls.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        if (names.Count == 0) { Status = Strings.SysDbg_PstoreNothingToPull; return; }

        var ok = 0;
        foreach (var name in names)
        {
            var r = await AppSession.Adb.PullAsync(serial, $"/sys/fs/pstore/{name}", dir);
            if (r.Success) ok++;
        }
        PstoreList += string.Format(Strings.SysDbg_PstorePulledFormat, ok, names.Count, dir);
        Status = string.Format(Strings.SysDbg_PstoreSavedFormat, dir);
    }

    private async Task ReadPropFileAsync()
    {
        var r = await RunShell($"cat {PropFilePath.Trim()}");
        if (!r.Success && r.StdOut.Trim().Length == 0)
        {
            Status = string.Format(Strings.SysDbg_ReadFailedFormat, r.Combined.Trim());
            return;
        }
        PropFileContent = r.StdOut;
        Status = string.Format(Strings.SysDbg_PropFileReadFormat, PropFilePath, PropFileContent.Length);
    }

    private async Task SavePropFileAsync()
    {
        if (MessageBox.Show(Strings.SysDbg_PropFileConfirm,
                Strings.SysDbg_HighRiskCaption, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
            ? string.Format(Strings.SysDbg_PropFileSavedFormat, path,
                $"{path}.bak", backup.Success ? Strings.SysDbg_BackupOk : Strings.SysDbg_BackupFailed)
            : string.Format(Strings.SysDbg_PropFileWriteFailedFormat, push.Combined.Trim());
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
        Status = r.Success
            ? Strings.SysDbg_CmdDone
            : string.Format(Strings.SysDbg_CmdExitFormat, r.ExitCode);
    }
}
