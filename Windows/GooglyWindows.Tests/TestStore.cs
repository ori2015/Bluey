using System.Collections.Concurrent;
using System.Text.Json;
namespace GooglyWindows.Security;
// Test-only storage double. Production uses DPAPI; these tests make no claim to exercise DPAPI.
public sealed class ProtectedStore
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "GooglyTestLogs");
    private readonly ConcurrentDictionary<string, byte[]> records = new();
    public Task<T?> ReadAsync<T>(string name, CancellationToken ct) => Task.FromResult(records.TryGetValue(name, out var value) ? JsonSerializer.Deserialize<T>(value) : default);
    public Task SaveAsync<T>(string name, T value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); records[name] = JsonSerializer.SerializeToUtf8Bytes(value); return Task.CompletedTask; }
}
