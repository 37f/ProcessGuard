using System.Windows;
using System.IO;
using ProcessGuard.Windows;
using ProcessGuard.App.ViewModels;
using ProcessGuard.App.Views;
namespace ProcessGuard.App;
public partial class App : Application
{
    private Mutex? instance;
    private readonly CancellationTokenSource stop = new();
    private SettingsGate? gate;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\ProcessGuard-75c2a07d-3a56-4ec1-8149-6a58780e160a", out bool first);
        if (!first) { MessageBox.Show("进程监控工具已在运行。", "ProcessGuard"); Shutdown(); return; }
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessGuard");
        var store = new SettingsStore(directory); var loaded = store.Load(); gate = new(loaded.Settings);
        string logs = Path.Combine(directory, "Logs");
        var vm = new MainViewModel(store, gate, new(new UserStartupRegistry()), Environment.ProcessPath!);
        if (loaded.Error != null) vm.Error = loaded.Error;
        var window = new MainWindow(vm, logs); MainWindow = window; window.Show();
        var monitor = new MonitorService(gate, new(gate, new AuditLog(logs), pid => new ProcessTarget(pid), Environment.ProcessId));
        _ = Task.Run(() => monitor.RunAsync(new(), frame => {
            if (!stop.IsCancellationRequested) Dispatcher.Invoke(() => vm.ApplyFrame(frame));
        }, error => { if (!stop.IsCancellationRequested) Dispatcher.Invoke(() => vm.ApplyMonitorFault(error)); }, stop.Token));
        window.Closing += (_, _) => { gate.Disable(); stop.Cancel(); };
    }
    protected override void OnExit(ExitEventArgs e)
    {
        gate?.Disable(); stop.Cancel(); instance?.Dispose();
        base.OnExit(e);
    }
}
