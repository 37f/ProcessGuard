using System.ComponentModel;
using System.IO;
using ProcessGuard.Core;
using ProcessGuard.Windows;
using ProcessGuard.App.ViewModels;
static class Program
{
    [STAThread] static int Main()
    {
        int fail = 0, count = 0;
        void Check(bool c) { if (!c) throw new Exception("assertion failed"); }
        void Test(string name, Action action) { count++; try { action(); Console.WriteLine("PASS " + name); } catch(Exception e) { fail++; Console.WriteLine("FAIL " + name + ": " + e); } }
        var temp = Path.Combine(Path.GetTempPath(), "ProcessGuard-UiTests-" + Guid.NewGuid());
        var gate = new SettingsGate(new()); var registry = new FakeRegistry();
        var vm = new MainViewModel(new(temp), gate, new(registry), "C:\\Test App\\ProcessGuard.exe");
        MonitorFrame Frame() => new(new(12, 45, 1000, [], 0), [
            new(new(new(10, 1), "alpha", 90, 300, 30), new(), "普通监控", false),
            new(new(new(11, 1), "beta", 1, 100, 10), new(), "黑名单", true)], false, "test");
        try {
            Test("Real window binds read-only metrics without startup exception", () => {
                var window = new ProcessGuard.App.Views.MainWindow(vm, temp) { ShowInTaskbar = false };
                try { window.Show(); window.UpdateLayout(); Check(window.IsVisible); }
                finally { window.Close(); }
            });
            Test("Rows refresh while filtered and sorted without losing priority", () => {
                vm.ApplyFrame(Frame()); vm.Sort("CpuPercent", ListSortDirection.Descending);
                Check(vm.Processes.Cast<ProcessRow>().First().Pid == 11);
                vm.SearchText = "alpha"; Check(vm.Processes.Cast<ProcessRow>().Count() == 1);
                vm.ApplyFrame(Frame()); Check(vm.Rows.Count == 2 && vm.Processes.Cast<ProcessRow>().Single().Pid == 10);
                vm.SearchText = "11"; Check(vm.Processes.Cast<ProcessRow>().Single().Pid == 11);
            });
            Test("Whitelist add persists and remove updates rules", () => {
                vm.AddWhitelist(" APP.exe "); Check(gate.Read().Settings.Whitelist!.Contains("app"));
                Check(new SettingsStore(temp).Load().Settings.Whitelist!.Contains("app"));
                vm.RemoveWhitelist("app"); Check(!gate.Read().Settings.Whitelist!.Contains("app"));
            });
            Test("Invalid names do not enter settings", () => {
                vm.AddBlacklist("*.exe"); Check(gate.Read().Settings.Blacklist!.Length == 0 && vm.Error.Length > 0);
            });
            Test("Auto switch persists but failed save fails closed", () => {
                vm.AutoTerminate = true; Check(gate.Read().Settings.AutoTerminate && new SettingsStore(temp).Load().Settings.AutoTerminate);
                var file = Path.Combine(temp, "file"); File.WriteAllText(file, "x");
                var badGate = new SettingsGate(new()); var bad = new MainViewModel(new(file), badGate, new(registry), "C:\\app.exe");
                bad.AutoTerminate = true; Check(!badGate.Read().Settings.AutoTerminate && bad.Error.Length > 0);
            });
            Test("Failed startup toggle rolls back UI", () => {
                registry.Fail = true; vm.StartupEnabled = true; Check(!vm.StartupEnabled && vm.Error.Length > 0);
            });
            Test("Monitor fault persists and notifies off state without another frame", () => {
                vm.AutoTerminate = true; var properties = new List<string?>();
                vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
                gate.Disable(); vm.ApplyMonitorFault("test sampling failure");
                Check(!new SettingsStore(temp).Load().Settings.AutoTerminate);
                Check(properties.Contains(nameof(vm.AutoTerminate)) && properties.Contains(nameof(vm.AutoStatus)));
            });
            Test("A frame persists fault disable after an enabled setting was saved", () => {
                vm.AutoTerminate = true; gate.Disable(); vm.ApplyFrame(Frame());
                Check(!new SettingsStore(temp).Load().Settings.AutoTerminate);
            });
        } finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        Console.WriteLine($"RESULT {count-fail}/{count} passed"); return fail == 0 ? 0 : 1;
    }
}
sealed class FakeRegistry : IStartupRegistry {
    public string? Value; public bool Fail;
    public string? Read() => Value;
    public void Write(string? command) { if (Fail) throw new UnauthorizedAccessException("test"); Value = command; }
}
