using Microsoft.Win32;
namespace ProcessGuard.Windows;
public interface IStartupRegistry { string? Read(); void Write(string? command); }
public sealed class StartupService(IStartupRegistry registry)
{
    public bool IsEnabled(string path) => string.Equals(registry.Read(), Quote(path), StringComparison.OrdinalIgnoreCase);
    public void SetEnabled(bool enabled, string path) => registry.Write(enabled ? Quote(path) : null);
    private static string Quote(string path) {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("需要完整的 EXE 路径");
        return "\"" + path + "\"";
    }
}
public sealed class UserStartupRegistry : IStartupRegistry
{
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ProcessGuard";
    public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(SubKey); return key?.GetValue(ValueName) as string; }
    public void Write(string? command) {
        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true);
        if (command == null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
