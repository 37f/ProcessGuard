using System.Diagnostics;
using System.Text.Json;
using ProcessGuard.Core;
using ProcessGuard.Windows;

if (args.Contains("--child")) { await Task.Delay(Timeout.Infinite); return 0; }
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> run) => tests.Add((name, run));
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
string temp = Path.Combine(Path.GetTempPath(), "ProcessGuard-Tests-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
Test("Settings roundtrip and normalization", () => {
    var s = new SettingsStore(Path.Combine(temp, "settings"));
    s.Save(new(true, false, [" App.exe ", "app", "中文程序"], ["other.exe"]));
    var (value, error) = s.Load();
    Check(error == null && value.AutoTerminate && !value.ProtectExplorer);
    Check(value.Whitelist!.SequenceEqual(new[] { "app", "中文程序" }));
});
Test("Corrupt settings fail closed", () => {
    var dir = Path.Combine(temp, "corrupt"); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{invalid");
    var (value, error) = new SettingsStore(dir).Load();
    Check(!value.AutoTerminate && error != null);
});
Test("JSON null settings fail closed", () => {
    var dir = Path.Combine(temp, "null"); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "null");
    Check(new SettingsStore(dir).Load().Error != null);
});
Test("Log has all audit fields and Unicode", () => {
    var dir = Path.Combine(temp, "log");
    new AuditLog(dir).Write(new("中文程序", 12, 82, 4096, 2, DateTimeOffset.Now, "CPU", "成功"));
    var line = File.ReadAllLines(Directory.GetFiles(dir).Single()).Single();
    using var json = JsonDocument.Parse(line);
    Check(json.RootElement.GetProperty("ProcessName").GetString() == "中文程序");
    foreach (var key in new[] { "Pid", "CpuPercent", "MemoryBytes", "MemoryPercent", "TriggerTime", "Reason", "Result" })
        Check(json.RootElement.TryGetProperty(key, out _), key);
});
Test("Log write errors surface", () => {
    var file = Path.Combine(temp, "not-a-directory"); File.WriteAllText(file, "x");
    bool failed = false;
    try { new AuditLog(file).Write(new("app", 12, 1, 1, 1, DateTimeOffset.Now, "test", "test")); }
    catch (IOException) { failed = true; }
    Check(failed);
});
Test("Startup quotes spaced executable and removes only own value", () => {
    var r = new FakeRegistry(); var startup = new StartupService(r);
    startup.SetEnabled(true, "C:\\A B\\App.exe");
    Check(r.Value == "\"C:\\A B\\App.exe\"");
    Check(startup.IsEnabled("C:\\A B\\App.exe"));
    startup.SetEnabled(false, "C:\\A B\\App.exe"); Check(r.Value == null);
});
Test("Startup failure is reported", () => {
    bool failed = false;
    try { new StartupService(new FakeRegistry { Fail = true }).SetEnabled(true, "C:\\app.exe"); }
    catch (UnauthorizedAccessException) { failed = true; }
    Check(failed);
});
var sample = new Sample(new(12345, 1), "testchild", 99, 42, 99);
var trigger = new RuleResult(11, 11, true, true);
foreach (var mode in new[] { "off", "white", "critical", "unknown", "identity", "version", "no-trigger", "log", "changed-during-log", "cancelled" }) {
    AsyncTest("Termination guard: " + mode, async () => {
        var gate = new SettingsGate(new(AutoTerminate: mode != "off", Whitelist: mode == "white" ? ["testchild"] : []));
        var target = new FakeTarget { Key = mode == "identity" ? new(12345, 2) : sample.Key,
            Critical = mode == "unknown" ? null : mode == "critical" };
        var log = new FakeLog { Fail = mode == "log" };
        if (mode == "changed-during-log") log.BeforeWrite = () => gate.Set(new(true, Whitelist: ["testchild"]));
        var terminator = new ProcessTerminator(gate, log, _ => target, 987);
        using var cts = new CancellationTokenSource(); if (mode == "cancelled") cts.Cancel();
        try { await terminator.TryTerminateAsync(sample, mode == "no-trigger" ? new() : trigger,
            mode == "version" ? 5 : 0, cts.Token); } catch (OperationCanceledException) { }
        Check(!target.Killed, "unsafe Kill occurred");
        if (mode == "log") Check(!gate.Read().Settings.AutoTerminate, "log error must disable auto termination");
    });
}
AsyncTest("Termination succeeds and writes before and after", async () => {
    var target = new FakeTarget { Key = sample.Key }; var log = new FakeLog();
    var result = await new ProcessTerminator(new(new(true)), log, _ => target, 987)
        .TryTerminateAsync(sample, trigger, 0, CancellationToken.None);
    Check(result.Killed && target.Killed && log.Entries.Count == 2);
    Check(log.Entries[0].Result == "准备结束" && log.Entries[1].Result == "已结束");
});
AsyncTest("Live sampler produces five valid snapshots", async () => {
    var sampler = new ProcessSampler();
    for (int i = 0; i < 5; i++) {
        var snapshot = sampler.Capture();
        Check(snapshot.TotalMemory > 0 && snapshot.Processes.Count > 0);
        Check(snapshot.MemoryPercent is >= 0 and <= 100);
        if (i > 0) Check(snapshot.CpuPercent is >= 0 and <= 100);
        Console.WriteLine($"  snapshot {i}: {snapshot.Processes.Count} processes; CPU {snapshot.CpuPercent:F1}; memory {snapshot.MemoryPercent:F1}");
        await Task.Delay(1000);
    }
});
AsyncTest("Live termination only ends owned child and records audit", async () => {
    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false, CreateNoWindow = true })!;
    try {
        await Task.Delay(300);
        var childSample = new Sample(new(child.Id, child.StartTime.ToUniversalTime().Ticks), child.ProcessName, 90, 100, 90);
        var directory = Path.Combine(temp, "child-audit");
        var result = await new ProcessTerminator(new(new(true)), new AuditLog(directory), pid => new ProcessTarget(pid), Environment.ProcessId)
            .TryTerminateAsync(childSample, trigger, 0, CancellationToken.None);
        Check(result.Killed && child.HasExited, result.Status);
        Check(File.ReadAllLines(Directory.GetFiles(directory).Single()).Length == 2);
    } finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
});
AsyncTest("Monitor prioritizes blacklist, limits actions and cools retries", async () => {
    var gate = new SettingsGate(new(true, Blacklist: ["testchild"]));
    var log = new FakeLog();
    var opened = new List<int>();
    double clock = 0;
    var terminator = new ProcessTerminator(gate, log, pid => { opened.Add(pid); return new FakeTarget { Key = new(pid, 1), Critical = true }; }, 987, () => clock);
    var monitor = new MonitorService(gate, terminator);
    var normal = sample with { Key = new(12346, 1), Name = "normal" };
    MonitorFrame? frame = null;
    for (int i = 0; i < 12; i++) { clock = i; frame = await monitor.ProcessSnapshotAsync(new(1, 1, 100, [normal, sample], i), CancellationToken.None); }
    Check(opened.SequenceEqual(new[] { 12345 }), "blacklisted process must go first and only one action per tick");
    Check(frame!.Processes[0].Sample.Key.Pid == 12345);
    clock = 12; await monitor.ProcessSnapshotAsync(new(1, 1, 100, [normal, sample], 12), CancellationToken.None);
    Check(opened.SequenceEqual(new[] { 12345, 12346 }), "failed attempt should be cooled");
});
AsyncTest("Monitor does not accumulate disabled time", async () => {
    var gate = new SettingsGate(new());
    var opened = new List<int>();
    var monitor = new MonitorService(gate, new(gate, new FakeLog(), pid => { opened.Add(pid); return new FakeTarget { Key = sample.Key }; }, 987));
    for (int i = 0; i < 20; i++) await monitor.ProcessSnapshotAsync(new(1, 1, 100, [sample], i), CancellationToken.None);
    gate.Set(new(true));
    var frame = await monitor.ProcessSnapshotAsync(new(1, 1, 100, [sample], 20), CancellationToken.None);
    Check(opened.Count == 0 && frame.Processes[0].Rule.CpuSeconds == 0);
    gate.Set(new(true, Whitelist: ["testchild"]));
    for (int i = 21; i < 40; i++) await monitor.ProcessSnapshotAsync(new(1, 1, 100, [sample], i), CancellationToken.None);
    Check(opened.Count == 0);
});
AsyncTest("Delayed audit cannot kill from an expired sample", async () => {
    double clock = 10;
    var target = new FakeTarget { Key = sample.Key };
    var log = new FakeLog { BeforeWrite = () => clock = 20 };
    var result = await new ProcessTerminator(new(new(true)), log, _ => target, 987, () => clock)
        .TryTerminateAsync(sample, trigger, 0, CancellationToken.None, 10);
    Check(!target.Killed && !result.Killed);
});
Test("Atomic settings edit preserves a prior fault disable", () => {
    var gate = new SettingsGate(new(true)); gate.Disable();
    AppSettings? saved = null;
    gate.Update(s => s with { Whitelist = ["app"] }, s => saved = s);
    Check(saved != null && !saved.AutoTerminate && saved.Whitelist!.Contains("app"));
    Check(!gate.Read().Settings.AutoTerminate);
});
Test("Atomic settings persist failure disables termination", () => {
    var gate = new SettingsGate(new()); bool error = false;
    try { gate.Update(s => s with { AutoTerminate = true }, _ => throw new IOException("test")); }
    catch (IOException) { error = true; }
    Check(error && !gate.Read().Settings.AutoTerminate);
});
int failed = 0;
try {
    foreach (var test in tests) {
        try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
        catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
    }
} finally { Directory.Delete(temp, recursive: true); }
Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;

sealed class FakeRegistry : IStartupRegistry {
    public string? Value; public bool Fail;
    public string? Read() => Value;
    public void Write(string? command) { if (Fail) throw new UnauthorizedAccessException(); Value = command; }
}
sealed class FakeTarget : IProcessTarget {
    public ProcessKey Key { get; set; }
    public string Name => "testchild";
    public bool? Critical { get; set; } = false;
    public bool? IsCritical => Critical;
    public bool Killed;
    public void Kill() => Killed = true;
    public Task<bool> WaitForExitAsync(CancellationToken token) => Task.FromResult(Killed);
    public void Dispose() { }
}
sealed class FakeLog : IAuditLog {
    public bool Fail; public Action? BeforeWrite;
    public List<AuditEntry> Entries = [];
    public void Write(AuditEntry entry) { BeforeWrite?.Invoke(); if (Fail) throw new IOException("test write failure"); Entries.Add(entry); }
}
