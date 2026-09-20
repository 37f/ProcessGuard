using ProcessGuard.Core;
using System.Text.Json;
namespace ProcessGuard.Windows;
public sealed class SettingsStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    private string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private static AppSettings Validate(AppSettings settings) => settings with {
        Whitelist = (settings.Whitelist ?? []).Select(ProtectionPolicy.NormalizeName).Distinct().ToArray(),
        Blacklist = (settings.Blacklist ?? []).Select(ProtectionPolicy.NormalizeName).Distinct().ToArray()
    };
    public (AppSettings Settings, string? Error) Load()
    {
        try {
            if (!File.Exists(FilePath)) return (new(), null);
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                ?? throw new JsonException("配置为空");
            return (Validate(settings), null);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException) {
            return (new(), "配置读取失败，自动结束已关闭：" + ex.Message);
        }
    }
    public void Save(AppSettings settings)
    {
        var value = Validate(settings);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
