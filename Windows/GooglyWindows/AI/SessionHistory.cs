using System.Text.Json;
using GooglyWindows.Settings;
using GooglyWindows.Security;
namespace GooglyWindows.AI;
public sealed class SessionHistory(AppSettings settings)
{
    private readonly SemaphoreSlim gate = new(1);
    private string session = Guid.NewGuid().ToString("N");
    public void Begin() => session = Guid.NewGuid().ToString("N");
    public async Task AddAsync(string kind, string text, CancellationToken ct)
    {
        if (!settings.History) return;
        await gate.WaitAsync(ct);
        try
        {
            var dir = Path.Combine(ProtectedStore.Root, "Sessions"); Directory.CreateDirectory(dir);
            await File.AppendAllTextAsync(Path.Combine(dir, session + ".jsonl"), JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, kind, text }) + "\n", ct);
        }
        finally { gate.Release(); }
    }
    public void DeleteAll()
    {
        var dir = Path.Combine(ProtectedStore.Root, "Sessions"); if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
