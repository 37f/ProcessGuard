using ProcessGuard.Core;
using System.Diagnostics;
namespace ProcessGuard.Windows;
public sealed record EvaluatedProcess(Sample Sample, RuleResult Rule, string Status, bool Blacklisted);
public sealed record MonitorFrame(SystemSnapshot System, IReadOnlyList<EvaluatedProcess> Processes,
    bool AutoTerminate, string LastAction);
public sealed class MonitorService(SettingsGate gate, ProcessTerminator terminator)
{
    private readonly ThresholdTracker tracker = new();
    private readonly Dictionary<ProcessKey, (double Time, string Status)> attempts = [];
    private long version = -1;
    private string lastAction = "尚无自动结束记录";
    public async Task<MonitorFrame> ProcessSnapshotAsync(SystemSnapshot snapshot, CancellationToken token)
    {
        var state = gate.Read();
        if (state.Version != version) { tracker.Reset(); attempts.Clear(); version = state.Version; }
        if (!state.Settings.AutoTerminate) tracker.Reset();
        var active = snapshot.Processes.Select(p => p.Key).ToHashSet();
        tracker.RemoveMissing(active);
        foreach (var key in attempts.Keys.Where(k => !active.Contains(k)).ToArray()) attempts.Remove(key);
        var rows = new List<EvaluatedProcess>();
        foreach (var sample in snapshot.Processes) {
            var protection = ProtectionPolicy.GetProtection(sample, state.Settings, Environment.ProcessId);
            var rule = protection == null && state.Settings.AutoTerminate
                ? tracker.Update(sample.Key, sample.CpuPercent, sample.MemoryPercent, snapshot.MonotonicSeconds) : new();
            bool black = ProtectionPolicy.IsBlacklisted(sample.Name, state.Settings);
            string status = protection ?? (sample.Error != null ? "部分数据不可读取" : black ? "黑名单 · 优先监控" : "普通监控");
            if (protection == null && attempts.TryGetValue(sample.Key, out var attempt) && snapshot.MonotonicSeconds - attempt.Time < 30)
                status = attempt.Status + "（30秒冷却）";
            rows.Add(new(sample, rule, status, black));
        }
        rows = rows.OrderByDescending(p => p.Blacklisted).ThenByDescending(p => p.Sample.CpuPercent).ToList();
        var candidate = rows.FirstOrDefault(p => p.Rule.Triggered &&
            (!attempts.TryGetValue(p.Sample.Key, out var attempt) || snapshot.MonotonicSeconds - attempt.Time >= 30));
        if (candidate != null && !token.IsCancellationRequested) {
            var result = await terminator.TryTerminateAsync(candidate.Sample, candidate.Rule, state.Version, token, snapshot.MonotonicSeconds).ConfigureAwait(false);
            if (result.ResetContinuity) tracker.Reset();
            else attempts[candidate.Sample.Key] = (snapshot.MonotonicSeconds, result.Status);
            lastAction = $"{DateTime.Now:HH:mm:ss}  {candidate.Sample.Name} / PID {candidate.Sample.Key.Pid}：{result.Status}";
        }
        return new(snapshot, rows, gate.Read().Settings.AutoTerminate, lastAction);
    }
    public async Task RunAsync(ProcessSampler sampler, Action<MonitorFrame> onFrame, Action<string> onError, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try {
            do {
                try {
                    var frame = await ProcessSnapshotAsync(sampler.Capture(), token).ConfigureAwait(false);
                    onFrame(frame);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    tracker.Reset(); gate.Disable(); onError("采样异常，自动结束已暂停：" + ex.Message);
                }
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
