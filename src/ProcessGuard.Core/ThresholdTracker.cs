namespace ProcessGuard.Core;
public sealed class ThresholdTracker
{
    private sealed class State { public double Last; public double? CpuStart; public double? MemoryStart; }
    private readonly Dictionary<ProcessKey, State> states = [];
    public RuleResult Update(ProcessKey key, double? cpu, double? memory, double now)
    {
        if (key.StartTimeUtcTicks <= 0 || !double.IsFinite(now)) return new();
        if (!states.TryGetValue(key, out var state) || now - state.Last > 2.5 || now <= state.Last)
            states[key] = state = new State();
        state.Last = now;
        state.CpuStart = Above(cpu) ? state.CpuStart ?? now : null;
        state.MemoryStart = Above(memory) ? state.MemoryStart ?? now : null;
        var c = state.CpuStart.HasValue ? now - state.CpuStart.Value : 0;
        var m = state.MemoryStart.HasValue ? now - state.MemoryStart.Value : 0;
        return new(c, m, c > 10, m > 10);
    }
    private static bool Above(double? value) => value.HasValue && double.IsFinite(value.Value) && value > 80;
    public void Reset() => states.Clear();
    public void RemoveMissing(IReadOnlySet<ProcessKey> active)
    {
        foreach (var key in states.Keys.Where(k => !active.Contains(k)).ToArray()) states.Remove(key);
    }
}
