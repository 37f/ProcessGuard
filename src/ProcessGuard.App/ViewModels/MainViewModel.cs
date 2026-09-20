using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using ProcessGuard.Core;
using ProcessGuard.Windows;
namespace ProcessGuard.App.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
public sealed class ProcessRow(EvaluatedProcess value) : Observable
{
    public EvaluatedProcess Value { get; private set; } = value;
    public ProcessKey Key => Value.Sample.Key;
    public int Pid => Key.Pid;
    public string Name => Value.Sample.Name;
    public double? CpuPercent => Value.Sample.CpuPercent;
    public double? MemoryMb => Value.Sample.WorkingSetBytes / 1048576.0;
    public double? MemoryPercent => Value.Sample.MemoryPercent;
    public string CpuText => CpuPercent.HasValue ? $"{CpuPercent:F1}%" : "—";
    public string MemoryText => MemoryMb.HasValue ? $"{MemoryMb:N1}" : "—";
    public string MemoryPercentText => MemoryPercent.HasValue ? $"{MemoryPercent:F2}%" : "—";
    public string Duration => $"CPU {Value.Rule.CpuSeconds:F0}s / 内存 {Value.Rule.MemorySeconds:F0}s";
    public string Status => Value.Status;
    public string? Error => Value.Sample.Error;
    public bool Blacklisted => Value.Blacklisted;
    public void Update(EvaluatedProcess next) { Value = next; Changed(""); }
}
public sealed class MainViewModel : Observable
{
    private readonly SettingsStore store;
    private readonly SettingsGate gate;
    private readonly StartupService startup;
    private readonly string executable;
    private bool savedAuto;
    private bool startupEnabled;
    private string searchText = "", error = "";
    public ObservableCollection<ProcessRow> Rows { get; } = [];
    public ObservableCollection<string> Whitelist { get; } = [];
    public ObservableCollection<string> Blacklist { get; } = [];
    public ICollectionView Processes { get; }
    public string CpuText { get; private set; } = "—";
    public string MemoryText { get; private set; } = "—";
    public double CpuValue { get; private set; }
    public double MemoryValue { get; private set; }
    public string PhysicalMemoryText { get; private set; } = "正在读取物理内存";
    public string ProcessCount { get; private set; } = "正在读取进程";
    public string LastAction { get; private set; } = "尚无自动结束记录";
    public string RefreshText { get; private set; } = "正在启动监控";
    public string AutoStatus => AutoTerminate ? "自动结束已开启" : "仅监控 · 自动结束关闭";
    public string Error { get => error; set { error = value; Changed(); } }
    public string SearchText { get => searchText; set { searchText = value; Processes.Refresh(); Changed(); } }
    public bool AutoTerminate { get => gate.Read().Settings.AutoTerminate; set => Save(s => s with { AutoTerminate = value }); }
    public bool ProtectExplorer { get => gate.Read().Settings.ProtectExplorer; set => Save(s => s with { ProtectExplorer = value }); }
    public bool StartupEnabled {
        get => startupEnabled;
        set {
            try { startup.SetEnabled(value, executable); startupEnabled = startup.IsEnabled(executable); Error = ""; }
            catch (Exception ex) { Error = "登录启动设置失败：" + ex.Message; }
            Changed();
        }
    }
    public MainViewModel(SettingsStore store, SettingsGate gate, StartupService startup, string executable)
    {
        this.store = store; this.gate = gate; this.startup = startup; this.executable = executable;
        savedAuto = gate.Read().Settings.AutoTerminate;
        Processes = CollectionViewSource.GetDefaultView(Rows);
        Processes.Filter = value => value is ProcessRow row &&
            (row.Name.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase) || row.Pid.ToString().Contains(searchText.Trim()));
        Sort("CpuPercent", ListSortDirection.Descending);
        RefreshLists();
        try { startupEnabled = startup.IsEnabled(executable); } catch (Exception ex) { Error = "无法读取登录启动状态：" + ex.Message; }
    }
    private void Save(Func<AppSettings, AppSettings> transform)
    {
        try {
            gate.Update(transform, settings => { store.Save(settings); savedAuto = settings.AutoTerminate; });
            Error = "";
        }
        catch (Exception ex) { Error = "设置保存失败，自动结束已关闭：" + ex.Message; }
        RefreshLists(); Changed(nameof(AutoTerminate)); Changed(nameof(AutoStatus)); Changed(nameof(ProtectExplorer));
    }
    private void RefreshLists()
    {
        var settings = gate.Read().Settings;
        Whitelist.Clear(); foreach (var item in settings.Whitelist ?? []) Whitelist.Add(item);
        Blacklist.Clear(); foreach (var item in settings.Blacklist ?? []) Blacklist.Add(item);
    }
    public void AddWhitelist(string name) => Add(name, true);
    public void AddBlacklist(string name) => Add(name, false);
    private void Add(string name, bool white)
    {
        try {
            var normalized = ProtectionPolicy.NormalizeName(name);
            Save(s => white ? s with { Whitelist = (s.Whitelist ?? []).Append(normalized).Distinct().ToArray() }
                : s with { Blacklist = (s.Blacklist ?? []).Append(normalized).Distinct().ToArray() });
        } catch (ArgumentException ex) { Error = ex.Message; }
    }
    public void RemoveWhitelist(string name) => Save(s => s with { Whitelist = (s.Whitelist ?? []).Where(n => n != name).ToArray() });
    public void RemoveBlacklist(string name) => Save(s => s with { Blacklist = (s.Blacklist ?? []).Where(n => n != name).ToArray() });
    public void Sort(string column, ListSortDirection direction) => ((ListCollectionView)Processes).CustomSort = new RowComparer(column, direction);
    public void ApplyFrame(MonitorFrame frame)
    {
        // ListCollectionView handles incremental additions/removals immediately.
        // DeferRefresh cannot enclose those collection change events.
        {
            var map = Rows.ToDictionary(r => r.Key);
            var active = frame.Processes.Select(r => r.Sample.Key).ToHashSet();
            for (int i = Rows.Count - 1; i >= 0; i--) if (!active.Contains(Rows[i].Key)) Rows.RemoveAt(i);
            foreach (var item in frame.Processes) {
                if (map.TryGetValue(item.Sample.Key, out var row)) row.Update(item);
                else Rows.Add(new(item));
            }
        }
        Processes.Refresh();
        CpuText = frame.System.CpuPercent.HasValue ? $"{frame.System.CpuPercent:F1}%" : "—";
        MemoryText = frame.System.MemoryPercent.HasValue ? $"{frame.System.MemoryPercent:F1}%" : "—";
        CpuValue = frame.System.CpuPercent ?? 0; MemoryValue = frame.System.MemoryPercent ?? 0;
        PhysicalMemoryText = $"物理内存 {frame.System.TotalMemory / 1073741824.0:F1} GB";
        ProcessCount = $"{Rows.Count} 个进程 · {Rows.Count(r => r.Error != null)} 个部分数据不可读";
        LastAction = frame.LastAction; RefreshText = $"1 秒刷新 · 最近采样 {DateTime.Now:HH:mm:ss}";
        // Persist fault-induced disable without accidentally restoring AutoTerminate at restart.
        if (savedAuto && !gate.Read().Settings.AutoTerminate) {
            try { store.Save(gate.Read().Settings); savedAuto = false; }
            catch (Exception ex) { Error = "无法保存暂停状态：" + ex.Message; }
        }
        if (frame.System.Error != null) Error = frame.System.Error;
        Changed("");
    }
    public void ApplyMonitorFault(string message)
    {
        try { gate.Update(s => s with { AutoTerminate = false }, store.Save); savedAuto = false; }
        catch (Exception ex) { message += "；暂停状态保存失败：" + ex.Message; }
        Error = message; Changed(nameof(AutoTerminate)); Changed(nameof(AutoStatus));
    }
    private sealed class RowComparer(string column, ListSortDirection direction) : IComparer
    {
        public int Compare(object? x, object? y) {
            if (x is not ProcessRow a || y is not ProcessRow b) return 0;
            var priority = b.Blacklisted.CompareTo(a.Blacklisted); if (priority != 0) return priority;
            int c = column switch {
                "Name" => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name),
                "Pid" => a.Pid.CompareTo(b.Pid),
                "MemoryMb" => Nullable.Compare(a.MemoryMb, b.MemoryMb),
                "MemoryPercent" => Nullable.Compare(a.MemoryPercent, b.MemoryPercent),
                "Status" => StringComparer.Ordinal.Compare(a.Status, b.Status),
                _ => Nullable.Compare(a.CpuPercent, b.CpuPercent)
            };
            return c == 0 ? a.Pid.CompareTo(b.Pid) : direction == ListSortDirection.Ascending ? c : -c;
        }
    }
}
