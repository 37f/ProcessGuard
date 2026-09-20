using ProcessGuard.Core;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
var key = new ProcessKey(12345, 1);
RuleResult Feed(ThresholdTracker t, double? cpu, double? memory, int start = 0, int end = 11)
{
    RuleResult result = new();
    for (int i = start; i <= end; i++) result = t.Update(key, cpu, memory, i);
    return result;
}
Test("CPU must exceed ten continuous seconds", () => {
    var t = new ThresholdTracker();
    Check(!Feed(t, 81, 0, end: 10).Triggered);
    Check(t.Update(key, 81, 0, 11).CpuTriggered);
});
Test("80 percent resets the timer", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 0);
    Check(!t.Update(key, 80, 0, 12).Triggered);
    Check(!t.Update(key, 81, 0, 13).Triggered);
});
Test("Memory triggers independently", () => Check(Feed(new(), 0, 81).MemoryTriggered));
Test("Alternating CPU and memory cannot combine", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 0, end: 6);
    Check(!Feed(t, 0, 81, start: 7, end: 13).Triggered);
});
Test("Unavailable CPU resets only CPU timer", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 81, end: 10);
    var r = t.Update(key, null, 81, 11); Check(!r.CpuTriggered && r.MemoryTriggered);
});
Test("Sampling gap resets continuity", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 81);
    Check(!t.Update(key, 81, 81, 15).Triggered);
});
Test("Clock regression resets continuity", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 81); Check(!t.Update(key, 81, 81, 1).Triggered);
});
Test("PID reuse starts a new timer", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 81);
    Check(!t.Update(new(key.Pid, 2), 81, 81, 12).Triggered);
});
Test("Reset and removal clear accumulated time", () => {
    var t = new ThresholdTracker(); Feed(t, 81, 81); t.Reset();
    Check(!t.Update(key, 81, 81, 12).Triggered);
    Feed(t, 81, 81, 13, 24); t.RemoveMissing(new HashSet<ProcessKey>());
    Check(!t.Update(key, 81, 81, 25).Triggered);
});
Test("NaN and unknown identity cannot trigger", () => {
    Check(!Feed(new(), double.NaN, double.NaN).Triggered);
    var t = new ThresholdTracker();
    for (int i = 0; i < 20; i++) Check(!t.Update(new(12, 0), 100, 100, i).Triggered);
});
Test("Name normalization and invalid entries", () => {
    Check(ProtectionPolicy.NormalizeName("  APP.ExE  ") == "app");
    foreach (var input in new[] { "", " ", "*.exe", "C:\\app.exe", "a/b", "a?b" }) {
        bool rejected = false;
        try { ProtectionPolicy.NormalizeName(input); } catch (ArgumentException) { rejected = true; }
        Check(rejected, input);
    }
});
Test("Permanent protection overrides blacklist", () => {
    foreach (var name in new[] { "System", "System Idle Process", "csrss.exe", "wininit", "services", "lsass", "smss", "winlogon" })
        Check(ProtectionPolicy.GetProtection(new(key, name, 99, 1, 99), new(Blacklist: [name]), 987) != null, name);
    foreach (int pid in new[] { 0, 4, 987 })
        Check(ProtectionPolicy.GetProtection(new(new(pid, 1), "other", 99, 1, 99), new(), 987) != null);
});
Test("Whitelist wins and Explorer protection is configurable", () => {
    Check(ProtectionPolicy.GetProtection(new(key, "APP.exe", 99, 1, 99), new(Whitelist: ["app"], Blacklist: ["APP"]), 987) != null);
    var sample = new Sample(key, "explorer", 99, 1, 99);
    Check(ProtectionPolicy.GetProtection(sample, new(), 987) != null);
    Check(ProtectionPolicy.GetProtection(sample, new(ProtectExplorer: false), 987) == null);
    Check(ProtectionPolicy.IsBlacklisted("APP.exe", new(Blacklist: ["app"])));
});
int failed = 0;
foreach (var test in tests) {
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
