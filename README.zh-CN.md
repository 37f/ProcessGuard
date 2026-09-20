# 进程哨兵 ProcessGuard — Windows 实时进程监控工具

v1.0.0：加入盾牌与监测脉冲图标，并统一中文窗口名称。图标包含 16、20、24、32、40、48、64、128、256 像素共九个尺寸，已嵌入 EXE、窗口与标题区。图标源文件位于 `src/ProcessGuard.App/Assets`，可运行 `powershell.exe -NoProfile -STA -File .\tools\New-Icon.ps1` 从矢量路径重新生成。

C# / .NET 8 / WPF。Windows 10/11 x64。自包含发布版不需要额外安装 .NET。程序每秒枚举系统可见进程，展示整机与进程 CPU、内存、保护状态及连续超限时间。

## 运行与使用

1. 将发布包解压到希望长期保存的位置，双击 `ProcessGuard.exe`。
2. 首次启动为“仅监控”，默认保护 Explorer。先在白名单加入需要保护的程序，例如 `word.exe`、`app.exe`（请以实际可执行文件名为准）。名称忽略大小写和可选 `.exe` 后缀，同名进程全部匹配。
3. 需要自动结束时勾选“开启自动结束”。自动结束、名单及 Explorer 保护会保存。开关重新打开、程序重启或规则变更时重新计时。
4. 黑名单进程在列表置顶并优先检查，但仍遵守保护、阈值及连续时长。白名单与黑名单重叠时，白名单优先。
5. 搜索支持名称或 PID；只过滤显示，后台仍监控全部可见进程。单击表头排序，黑名单依然优先。小窗口可横向滚动表格、纵向滚动设置区。
6. “当前用户登录时启动”使用当前用户 Run 注册表项。它不是系统服务，不在登录前运行。程序移动后重新设置此选项。
7. 关闭窗口退出程序并停止监控；最小化仍继续监控。没有托盘驻留功能。

自动结束会强制退出进程，可能丢失未保存的数据。程序不会尝试绕过 Windows 权限；部分进程只有管理员才能读取或结束，可按需手动以管理员身份启动。

## 计算口径与触发条件

| 指标 | 计算方法 |
|---|---|
| 单进程 CPU | CPU 时间增量 ÷ 实际采样秒数 ÷ 全部逻辑处理器数量 × 100% |
| 单进程内存 | WorkingSet64（工作集）÷ 整机物理内存总量 × 100% |
| 整机 CPU | GetSystemTimes 的内核、用户与空闲时间增量 |
| 整机内存 | GlobalMemoryStatusEx 的已使用物理内存比例 |

两个进程级规则分别计时：CPU **严格大于 80%** 或内存 **严格大于 80%**，对应连续时间**严格超过 10 秒**才触发。第一次超限采样记为起点；一秒采样时通常约在第 11 秒之后触发。整机内存达到 80% 并不会单独触发结束；例如 16 GB 内存，单个进程约超过 12.8 GB 才符合内存条件。

等于阈值、回落、读数失败、身份变化、采样间隔超过 2.5 秒会重置对应计时。第一帧 CPU 为“—”。不累计间歇超限时间。轮询只能依据采样值判断，无法保证观察两次采样之间的全部瞬时变化。

工作集包括共享内存，可能与任务管理器显示的“专用工作集”不同，各进程内存不能简单相加。CPU 口径也可能与任务管理器的处理器效用指标不同。Windows 禁止读取的数据展示为“—”或不可读取状态；不会假装已读取全部保护进程的数值。

## 保护和异常处理

- 永久保护 PID 0/4、工具自身、System、System Idle Process、csrss、wininit、services、lsass、smss、winlogon 等关键名称。
- Explorer 默认保护，可配置。白名单始终优先。
- 结束前重新验证 PID + 启动时间及 Windows 的 IsProcessCritical 标志。关键标志未知、关键进程或身份不可确认都不结束。
- 使用持有的 Process 句柄结束目标进程，不递归结束整个进程树。
- 每个采样轮次最多处理一个候选，黑名单优先；失败或退出未确认后的同一身份冷却 30 秒，期间继续采样。退出确认最多等待 500 毫秒。
- 日志失败、采样异常时暂停自动结束；配置损坏时以自动结束关闭的默认设置启动，并显示错误。
- 名称白名单用于防止误结束，不是安全隔离机制。黑名单也不是恶意软件检测。

## 设置与日志位置

```text
%LOCALAPPDATA%\ProcessGuard\settings.json
%LOCALAPPDATA%\ProcessGuard\Logs\yyyy-MM-dd.jsonl
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ProcessGuard
```

日志为 UTF-8 JSON Lines，含 ProcessName、Pid、CpuPercent、MemoryBytes、MemoryPercent、TriggerTime（含时区）、Reason、Result、Error。正常采样不逐秒写日志；触发时先写“准备结束”，再写结果。日志不自动清理，可在程序关闭后按需归档。

关闭登录启动时仅删除本工具的注册表值。移除工具前建议先在界面关闭登录启动，再退出程序。需要完全清除用户数据时，可自行删除上述专用目录。

## 编译

安装 .NET 8 SDK 或支持 net8.0 的更新 SDK（运行时不等于 SDK），在 Windows PowerShell 中进入源码根目录：

```powershell
dotnet --version
.\build.ps1
```

本地 SDK 或自定义发布路径：

```powershell
.\build.ps1 -Dotnet 'D:\Tools\dotnet\dotnet.exe' -Output 'D:\Build\ProcessGuard'
```

只编译和发布：

```powershell
dotnet build .\src\ProcessGuard.App -c Release
dotnet publish .\src\ProcessGuard.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\publish
```

完整 build.ps1 会依次运行规则、Windows 集成、WPF 测试后发布。集成测试只结束其自己创建的等待子进程；WPF 测试会短暂显示测试窗口。测试使用临时配置和假的启动注册表，不修改实际登录启动项。程序没有第三方运行依赖；首次恢复/发布可能需要联网下载 Microsoft 的运行时包。

## 源码结构

```text
src/ProcessGuard.Core/        数据模型、阈值计时、保护策略
src/ProcessGuard.Windows/     系统采样、终止、监控循环、配置、日志、登录启动
src/ProcessGuard.App/         WPF 入口、ViewModels、Views
tests/ProcessGuard.Tests/     确定性规则测试
tests/ProcessGuard.Integration/ Windows 服务及测试子进程验证
tests/ProcessGuard.UiTests/   真实窗口绑定和界面状态测试
build.ps1                    构建、测试、自包含发布
```

扩展阈值可从 ThresholdTracker 入手；采样、终止与 UI 分离。修改阈值时应同步调整界面文字、规则说明和边界测试。

参考：[微软 .NET Windows 安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)、[IsProcessCritical](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-isprocesscritical)。实际验证范围详见随交付提供的验证报告。
