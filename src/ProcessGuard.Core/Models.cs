namespace ProcessGuard.Core;

public readonly record struct ProcessKey(int Pid, long StartTimeUtcTicks);
public sealed record Sample(ProcessKey Key, string Name, double? CpuPercent,
    long? WorkingSetBytes, double? MemoryPercent, string? Error = null);
public sealed record RuleResult(double CpuSeconds = 0, double MemorySeconds = 0,
    bool CpuTriggered = false, bool MemoryTriggered = false)
{
    public bool Triggered => CpuTriggered || MemoryTriggered;
    public string Reason => string.Join("；", new[] {
        CpuTriggered ? "CPU > 80% 持续超过 10 秒" : null,
        MemoryTriggered ? "进程内存 > 物理内存的 80% 持续超过 10 秒" : null
    }.Where(s => s != null));
}
public sealed record AppSettings(bool AutoTerminate = false, bool ProtectExplorer = true,
    string[]? Whitelist = null, string[]? Blacklist = null)
{
    public AppSettings Copy() => this with {
        Whitelist = Whitelist?.ToArray() ?? [], Blacklist = Blacklist?.ToArray() ?? [] };
}
