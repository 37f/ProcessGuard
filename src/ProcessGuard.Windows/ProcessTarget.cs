using System.Diagnostics;
using ProcessGuard.Core;
namespace ProcessGuard.Windows;

public sealed class ProcessTarget : IProcessTarget
{
    private readonly Process process;
    public ProcessKey Key { get; }
    public string Name { get; }
    public ProcessTarget(int pid)
    {
        process = Process.GetProcessById(pid);
        try {
            // Cache a real handle before reading identity and retain it through Kill.
            // Keeping this handle open prevents PID reuse while we operate.
            _ = process.SafeHandle;
            Key = new(pid, process.StartTime.ToUniversalTime().Ticks);
            Name = process.ProcessName;
        } catch { process.Dispose(); throw; }
    }
    public bool? IsCritical => NativeMethods.IsProcessCritical(process.SafeHandle, out var critical) ? critical : null;
    public void Kill() => process.Kill(entireProcessTree: false);
    public async Task<bool> WaitForExitAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(500);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return process.HasExited; }
    }
    public void Dispose() => process.Dispose();
}
