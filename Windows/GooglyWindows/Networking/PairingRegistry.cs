using System.Security.Cryptography;
using GooglyWindows.Core.Security;
using GooglyWindows.Security;
namespace GooglyWindows.Networking;
public sealed record PairedDevice(string ID, string Name, string Secret);
public sealed class PairingRegistry(ProtectedStore store)
{
    private readonly SemaphoreSlim gate = new(1);
    private List<PairedDevice> devices = [];
    private string? code;
    private DateTimeOffset expires;
    private int attempts;
    public string? Code => expires > DateTimeOffset.UtcNow ? code : null;
    public IReadOnlyList<PairedDevice> Devices => devices.ToArray();
    public async Task LoadAsync(CancellationToken ct) => devices = await store.ReadAsync<List<PairedDevice>>("paired-devices", ct) ?? [];
    public string BeginPairing()
    {
        gate.Wait();
        try { code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); expires = DateTimeOffset.UtcNow.AddMinutes(2); attempts = 0; return code; }
        finally { gate.Release(); }
    }
    public async Task<PairedDevice?> PairAsync(string id, string name, string? supplied, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!Guid.TryParse(id, out _) || id.Length > 40 || name.Length > 100 || Code is null || attempts++ >= 5) return null;
            try { Core.Auth.OAuthPrimitives.ValidateState(code!, supplied); } catch (InvalidDataException) { return null; }
            var device = new PairedDevice(id, name, Pairing.NewSecret());
            var next = devices.Where(d => d.ID != id).Append(device).ToList();
            await store.SaveAsync("paired-devices", next, ct); devices = next; code = null; return device;
        }
        finally { gate.Release(); }
    }
    public bool Authenticate(string host, string id, string nonce, string? proof) => devices.FirstOrDefault(d => d.ID == id) is { } d && Pairing.Verify(d.Secret, host, id, nonce, proof);
    public async Task ForgetAllAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct); try { await store.SaveAsync("paired-devices", Array.Empty<PairedDevice>(), ct); devices = []; code = null; } finally { gate.Release(); }
    }
}
