namespace ProcessGuard.Core;
public static class ProtectionPolicy
{
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    { "system", "system idle process", "idle", "registry", "secure system", "memory compression",
      "csrss", "wininit", "services", "lsass", "smss", "winlogon" };
    public static string NormalizeName(string name)
    {
        var result = name.Trim().ToLowerInvariant();
        if (result.EndsWith(".exe", StringComparison.Ordinal)) result = result[..^4];
        if (string.IsNullOrWhiteSpace(result) || result.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || result.Any(char.IsControl))
            throw new ArgumentException("请输入程序文件名，例如 app.exe；不支持路径或通配符。");
        return result;
    }
    private static bool Matches(string name, string[]? list) =>
        list?.Any(n => string.Equals(NormalizeName(n), name, StringComparison.OrdinalIgnoreCase)) == true;
    public static string? GetProtection(Sample sample, AppSettings settings, int ownPid)
    {
        if (sample.Key.Pid is 0 or 4 || sample.Key.Pid == ownPid) return "系统 / 自身保护";
        if (sample.Key.StartTimeUtcTicks <= 0) return "身份不可确认";
        var name = NormalizeName(sample.Name);
        if (CriticalNames.Contains(name)) return "系统关键进程保护";
        if (settings.ProtectExplorer && name == "explorer") return "Explorer 保护";
        if (Matches(name, settings.Whitelist)) return "白名单保护";
        return null;
    }
    public static bool IsBlacklisted(string name, AppSettings settings) => Matches(NormalizeName(name), settings.Blacklist);
}
