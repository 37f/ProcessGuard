# 进程哨兵 · ProcessGuard

<img src="src/ProcessGuard.App/Assets/ProcessGuard.png" width="100" alt="进程哨兵图标" />

基于 **C# / .NET 8 / WPF** 的 Windows 实时进程监控工具。每秒刷新 CPU 和内存，按持续超限规则自动结束进程，并提供关键进程保护、白名单、黑名单和审计日志。

## 下载

**[下载 Windows x64 版 v1.0.0](https://github.com/37f/ProcessGuard/releases/download/v1.0.1/ProcessGuard-v1.0.1-win-x64.zip)**

解压后运行 `ProcessGuard.exe`。发布版自带 .NET 运行库，无需另行安装。

[全部 Release](https://github.com/37f/ProcessGuard/releases) · [完整中文使用与编译说明](README.zh-CN.md)

## 功能

- 每秒刷新整机与单进程 CPU、物理内存使用情况。
- CPU 与内存分别计时，单进程严格超过 80% 且连续超过 10 秒才触发结束。
- 内存比例采用 **单进程工作集 / 整机物理内存**，不是整机内存压力触发。
- 系统关键进程及工具自身受保护，Explorer 默认保护且可配置。
- 白名单优先于黑名单；黑名单置顶且优先处理，仍须满足阈值。
- 支持名称/PID 搜索、排序、当前用户登录启动、JSONL 日志。

**首次启动默认仅监控。** 开启自动结束前请添加需要保护的程序；强制退出可能导致未保存数据丢失。Windows 拒绝访问的数据会标记为不可用，程序不会绕过权限。

## 编译

在 Windows 安装 .NET 8 SDK 后：

```powershell
git clone https://github.com/37f/ProcessGuard.git
cd ProcessGuard
.\build.ps1
```

脚本依次运行测试并发布到 `publish`。也可以在 Visual Studio 中打开 `ProcessGuard.sln`，以 `ProcessGuard.App` 为启动项目。

## 结构

| 目录 | 职责 |
|---|---|
| `src/ProcessGuard.Core` | 数据模型、连续阈值判定、保护策略 |
| `src/ProcessGuard.Windows` | 系统采样、进程终止、设置、日志、登录启动 |
| `src/ProcessGuard.App` | WPF 界面及 ViewModel |
| `tests` | 规则、Windows 集成及 WPF 测试 |
| `tools/New-Icon.ps1` | 多尺寸 ICO 与 SVG/PNG 生成 |

## 验证

v1.0.0 发布前 **46 项测试通过**：13 项规则测试、25 项 Windows 集成测试、8 项 WPF 测试。已实际启动发布版，并对专门创建的子进程验证终止功能。

未进行实际 80% 内存高压测试、重启登录测试或跨硬件长期稳定性测试。阈值时序采用虚拟时间测试，登录启动写入行为采用隔离测试。详情见[中文说明](README.zh-CN.md)。
