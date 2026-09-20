using ProcessGuard.Core;
namespace ProcessGuard.Windows;

public sealed record SystemSnapshot(double? CpuPercent, double? MemoryPercent, ulong TotalMemory,
    IReadOnlyList<Sample> Processes, double MonotonicSeconds, string? Error = null);
public sealed record AuditEntry(string ProcessName, int Pid, double? CpuPercent, long? MemoryBytes,
    double? MemoryPercent, DateTimeOffset TriggerTime, string Reason, string Result, string? Error = null);
public interface IAuditLog { void Write(AuditEntry entry); }
public interface IProcessTarget : IDisposable
{
    ProcessKey Key { get; }
    string Name { get; }
    bool? IsCritical { get; }
    void Kill();
    Task<bool> WaitForExitAsync(CancellationToken token);
}
public sealed record TerminationResult(bool Killed, string Status, bool ResetContinuity = false);

// The same gate serializes settings changes and the final Kill decision.
public sealed class SettingsGate(AppSettings settings)
{
    public object Sync { get; } = new();
    private AppSettings current = settings.Copy();
    private long version;
    public (AppSettings Settings, long Version) Read() { lock (Sync) return (current.Copy(), version); }
    public void Set(AppSettings value) { lock (Sync) { current = value.Copy(); version++; } }
    public void Disable() { lock (Sync) { current = current with { AutoTerminate = false }; version++; } }
    public void Update(Func<AppSettings, AppSettings> transform, Action<AppSettings> persist)
    {
        lock (Sync) {
            current = transform(current.Copy()).Copy(); version++;
            try { persist(current.Copy()); }
            catch { current = current with { AutoTerminate = false }; version++; throw; }
        }
    }
}
