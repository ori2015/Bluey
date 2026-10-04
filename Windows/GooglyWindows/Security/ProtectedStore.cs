using System.Security.Cryptography;
using System.Text.Json;
namespace GooglyWindows.Security;
public sealed class ProtectedStore
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GooglyEyes");
    private readonly SemaphoreSlim gate = new(1);
    public async Task<T?> ReadAsync<T>(string name, CancellationToken ct)
    {
        var file = Path.Combine(Root, name + ".dpapi");
        if (!File.Exists(file)) return default;
        var encrypted = await File.ReadAllBytesAsync(file, ct);
        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<T>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task SaveAsync<T>(string name, T value, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Root);
            var plain = JsonSerializer.SerializeToUtf8Bytes(value);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            var file = Path.Combine(Root, name + ".dpapi"); var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, encrypted, ct); File.Move(temp, file, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
    }
}
