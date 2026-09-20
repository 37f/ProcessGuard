using ProcessGuard.Core;
using System.Diagnostics;
namespace ProcessGuard.Windows;
public sealed class ProcessTerminator(SettingsGate gate, IAuditLog log,
    Func<int, IProcessTarget> open, int ownPid, Func<double>? monotonicClock = null)
{
    public async Task<TerminationResult> TryTerminateAsync(Sample sample, RuleResult result,
        long expectedVersion, CancellationToken token, double? sampledAt = null)
    {
        double Clock() => monotonicClock?.Invoke() ?? Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        double sampled = sampledAt ?? Clock();
        bool Fresh() { double age = Clock() - sampled; return double.IsFinite(age) && age >= 0 && age <= 2.5; }
        bool CanAct(Sample actual) {
            var (settings, version) = gate.Read();
            return !token.IsCancellationRequested && settings.AutoTerminate && version == expectedVersion &&
                result.Triggered && ProtectionPolicy.GetProtection(actual, settings, ownPid) == null;
        }
        var entry = new AuditEntry(sample.Name, sample.Key.Pid, sample.CpuPercent, sample.WorkingSetBytes,
            sample.MemoryPercent, DateTimeOffset.Now, result.Reason, "准备结束");
        bool Write(AuditEntry value) {
            try { log.Write(value); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                gate.Disable(); return false;
            }
        }
        if (!Fresh()) return new(false, "采样已过期，重新计时", true);
        if (!CanAct(sample)) return new(false, "已跳过：开关、规则或保护状态不允许");
        try {
            using var target = open(sample.Key.Pid);
            var actual = sample with { Key = target.Key, Name = target.Name };
            string? skipped = null;
            lock (gate.Sync) {
                if (!Fresh()) skipped = "采样已过期，重新计时";
                else if (target.Key != sample.Key) skipped = "进程身份已变化";
                else if (!CanAct(actual)) skipped = "保护规则或开关已变化";
                else if (target.IsCritical != false) skipped = "Windows 关键进程或关键标志不可读取";
                if (skipped != null) {
                    if (!Write(entry with { Result = "已跳过", Error = skipped })) return new(false, "日志写入失败，自动结束已暂停");
                    return new(false, "已跳过：" + skipped, !Fresh());
                }
                if (!Write(entry)) return new(false, "日志写入失败，自动结束已暂停");
                // Settings may change through callbacks while logging; check again immediately before Kill.
                if (!CanAct(actual) || !Fresh()) {
                    string reason = !Fresh() ? "采样已过期，重新计时" : "设置已变化或监控已停止";
                    Write(entry with { Result = "已取消", Error = reason });
                    return new(false, "已取消：" + reason, !Fresh());
                }
                target.Kill();
            }
            bool exited = await target.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var status = exited ? "已结束" : "已发送结束请求，但尚未确认退出";
            if (!Write(entry with { Result = status })) return new(exited, status + "；结果日志失败，自动结束已暂停");
            return new(exited, status);
        } catch (Exception ex) when (ex is not OutOfMemoryException) {
            if (!Write(entry with { Result = "结束失败", Error = ex.Message })) return new(false, "日志写入失败，自动结束已暂停");
            return new(false, "结束失败：" + ex.Message);
        }
    }
}
