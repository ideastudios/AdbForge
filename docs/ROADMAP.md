# 功能路线图

所有功能均基于 adb 命令实现。✅ 已实现 · 🔜 规划中

## 已实现 ✅

### 设备管理页
| 功能 | 底层命令 |
|---|---|
| 设备列表与状态、全局设备上下文 | `adb devices -l` |
| 无线连接 / 配对（Android 11+） | `adb connect / pair` |
| 重启到系统 / Recovery / Bootloader | `adb reboot [mode]` |

### 日志 Logcat 页
| 功能 | 底层命令 |
|---|---|
| Logcat 查看器（7 种 buffer/级别/Tag/关键字/着色/保存） | `adb logcat -v threadtime -b <buf> *:L` |
| 清空缓冲区 | `adb logcat -b all -c` |

### 应用管理页
| 功能 | 底层命令 |
|---|---|
| 应用列表（第三方/系统）+ 搜索 | `adb shell pm list packages -f [-3|-s]` |
| APK 安装（替换/授权/测试包）+ 错误码翻译 | `adb install -r -t -g` |
| 应用卸载（保留数据） | `adb uninstall [-k]` |

### 文件传输页
| 功能 | 底层命令 |
|---|---|
| 设备目录浏览（双击进入、上级、常用目录） | `shell ls -la`（toybox/toolbox 双格式解析） |
| 推送文件/文件夹到当前目录 | `adb push` |
| 拉取选中项/当前路径到本机并打开 | `adb pull` |
| 新建目录 / 递归删除 | `shell mkdir / rm -r` |

### 屏幕与输入页
| 功能 | 底层命令 |
|---|---|
| 截图（exec-out 二进制直传） | `adb exec-out screencap -p` |
| 录屏（码率/时长/分辨率，停止自动拉取） | `shell screenrecord` |
| 快捷键面板（13 个：亮灭屏/返回/主页/音量/解锁/通知栏…） | `input keyevent` / `wm dismiss-keyguard` / `cmd statusbar` |
| 文本注入（ASCII） | `input text` |
| 坐标点击 / 滑动 | `input tap / swipe` |
| UI 层级 dump 到本机 | `shell uiautomator dump` |
| Monkey 压测（包名/事件数/节流/seed，日志实时滚动） | `shell monkey` |

### 性能诊断页
| 功能 | 底层命令 |
|---|---|
| 设备信息全景卡（型号/系统/序列号/ABI/屏幕/堆限制） | `getprop` + `wm size/density` |
| 电量面板（状态/温度/电压）+ 自动刷新 | `dumpsys battery` |
| 虚拟电量（模拟电量/拔出/恢复） | `dumpsys battery set level / unplug / reset` |
| 内存分析 | `dumpsys meminfo <pkg>` |
| 启动时间批量测试（N 次均值/最快/最慢） | `am start -W` |
| 卡顿分析（总帧/卡顿帧/百分比） | `dumpsys gfxinfo <pkg>` |
| 硬件状态（CPU 各 cluster 频率/GPU devfreq/全部温区） | `cat /sys/.../scaling_cur_freq` 等 sysfs |
| 崩溃 / ANR 历史（DropBox 列表 + 查看最新） | `dumpsys dropbox [--print]` |

### 系统调试页
| 功能 | 底层命令 |
|---|---|
| Settings 浏览器（system/secure/global 搜索 + get/put/delete） | `settings list/get/put/delete` |
| getprop 查看器（搜索/导出 + 常用快查表） | `adb shell getprop` |
| Intent 构造器（action/URI/component/extras 表单） | `am start -a -d -n --es/--ei/--ez` |
| 调试预设包（动画归零/恢复、保持唤醒、自由窗口、NTP、横竖屏…） | `settings put global ...` |
| SELinux 查看与切换（带确认） | `getenforce` / `setenforce 0\|1` |
| ADB 网络调试持久化 | `setprop service.adb.tcp.port` + `stop/start adbd` |
| pstore 内核恐慌日志（查看 + 拉取全部） | `ls / pull /sys/fs/pstore/*` |
| build.prop 查看 / 编辑（高危确认 + 自动备份 .bak） | `cat` + `adb root/remount` + `push` |
| ADB 终端（历史记录、回车执行） | `adb shell <任意命令>` |

### 更多工具页
| 功能 | 底层命令 |
|---|---|
| 输入法列表与切换 | `ime list -s / ime set` |
| 通知测试器 + 通知列表查看 | `cmd notification post` / `dumpsys notification` |
| 设备全局代理设置/清除 | `settings put/delete global http_proxy` |
| 多用户（列表/创建/删除/当前用户） | `pm list users / create-user / remove-user`、`am get-current-user` |
| 运行时权限授予/撤销 | `pm grant / revoke` |
| Bugreport 一键采集到桌面 | `adb bugreport <zip>` |
| ADB Server 启停 | `adb kill-server / start-server` |
| Sideload 刷 OTA（重启→等待→推送） | `adb reboot sideload / wait-for-sideload / sideload` |
| 设备端后台日志（拔线采集，启动/停止/拉取） | `shell "nohup logcat -b <buf> > /sdcard/..." &` |

### 设置页
| 功能 | 底层命令 |
|---|---|
| adb.exe 路径配置与自动探测、版本检测 | `adb version` |

## 规划中 🔜

| 功能 | 底层命令 | 备注 |
|---|---|---|
| 包名→PID 无感过滤 logcat | `pidof -s` + `logcat --pid` | Logcat 页 |
| 免 root 瘦身（停用预装 + 一键恢复） | `pm uninstall --user 0` 等 | 应用管理页 |
| run-as 免 root 应用数据访问 | `run-as <pkg>` | 文件传输页 |
| 多设备并行执行 | `adb -s` 循环 | 全局 |
| 实时设备监听替换轮询 | `adb track-devices` 协议 | 全局 |
| 独立 adb server 端口 | `adb -P <port> start-server` | 设置页 |
| Perfetto trace 一键抓取 | `perfetto` 命令 | 性能页 |
| 卡顿逐帧分析（framestats） | `dumpsys gfxinfo framestats` | 性能页 |
| UI 自动化元素点击 | `uiautomator dump` 解析 XML | 屏幕页 |
