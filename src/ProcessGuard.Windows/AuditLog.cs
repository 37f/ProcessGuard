using System.Text;
using System.Text.Json;
namespace ProcessGuard.Windows;
public sealed class AuditLog(string directory) : IAuditLog
{
    public string DirectoryPath { get; } = directory;
    private readonly object sync = new();
    public void Write(AuditEntry entry)
    {
        lock (sync) {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"{DateTime.Now:yyyy-MM-dd}.jsonl");
            // Flush before a Kill is permitted; a write error must fail closed.
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + Environment.NewLine);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }
}
