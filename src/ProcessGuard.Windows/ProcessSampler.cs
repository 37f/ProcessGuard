using System.Diagnostics;
using System.Runtime.InteropServices;
using ProcessGuard.Core;
namespace ProcessGuard.Windows;
public sealed class ProcessSampler
{
    private readonly Dictionary<ProcessKey, (double Cpu, double Time)> previous = [];
    private (ulong Idle, ulong Total)? systemPrevious;
    private readonly int cores = Math.Max(1, (int)NativeMethods.GetActiveProcessorCount(0xffff));
    public SystemSnapshot Capture()
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var memory = new NativeMethods.MemoryStatus { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>() };
        var memoryOk = NativeMethods.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0;
        double? globalMemory = memoryOk ? (1 - memory.AvailablePhysical / (double)memory.TotalPhysical) * 100 : null;
        double? cpu = null;
        var timeOk = NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user);
        if (timeOk) {
            var total = kernel + user;
            if (systemPrevious is { } old && total > old.Total && idle >= old.Idle)
                cpu = Math.Clamp(100 * (1 - (idle - old.Idle) / (double)(total - old.Total)), 0, 100);
            systemPrevious = (idle, total);
        } else systemPrevious = null;
        var rows = new List<Sample>();
        var seen = new HashSet<ProcessKey>();
        foreach (var process in Process.GetProcesses()) {
            using (process) {
                int pid = process.Id;
                string name = $"PID {pid}";
                long start = 0;
                long? workingSet = null;
                double? usage = null;
                string? error = null;
                try { name = process.ProcessName; } catch (Exception ex) { error = ex.Message; }
                try { start = process.StartTime.ToUniversalTime().Ticks; } catch (Exception ex) { error = ex.Message; }
                var key = new ProcessKey(pid, start);
                seen.Add(key);
                try {
                    double seconds = process.TotalProcessorTime.TotalSeconds;
                    if (start > 0) {
                        if (previous.TryGetValue(key, out var p) && now > p.Time && now - p.Time <= 2.5 && seconds >= p.Cpu)
                            usage = Math.Clamp((seconds - p.Cpu) / (now - p.Time) / cores * 100, 0, 100);
                        previous[key] = (seconds, now);
                    }
                } catch (Exception ex) { previous.Remove(key); error = ex.Message; }
                try { workingSet = process.WorkingSet64; } catch (Exception ex) { error = ex.Message; }
                double? percent = memoryOk && workingSet.HasValue ? workingSet.Value / (double)memory.TotalPhysical * 100 : null;
                rows.Add(new(key, name, usage, workingSet, percent, error));
            }
        }
        foreach (var key in previous.Keys.Where(k => !seen.Contains(k)).ToArray()) previous.Remove(key);
        return new(cpu, globalMemory, memoryOk ? memory.TotalPhysical : 0, rows, now,
            memoryOk && timeOk ? null : "部分整机性能数据不可读取");
    }
}
