using System.Security.Cryptography;
using System.Text;
namespace GooglyWindows.Core.Auth;

public static class OAuthPrimitives
{
    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static void ValidateState(string expected, string? actual)
    {
        if (string.IsNullOrEmpty(actual) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
            throw new InvalidDataException("Sign-in state did not match.");
    }
    public static string IssuedClient(string? saved, string? returned)
    {
        if (saved is not null && returned is not null && saved != returned) throw new InvalidDataException("Client registration changed.");
        var issued = returned ?? saved;
        if (string.IsNullOrWhiteSpace(issued) || issued == "dynamic_agent_client") throw new InvalidDataException("Missing issued client ID.");
        return issued;
    }
}

public sealed class RefreshGate<T>(Func<CancellationToken, Task<T>> read, Func<T, bool> expired,
    Func<T, CancellationToken, Task<T>> refresh, Func<T, CancellationToken, Task> save, SemaphoreSlim? sharedGate = null)
{
    private readonly SemaphoreSlim gate = sharedGate ?? new(1);
    public async Task<T> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = await read(ct);
            if (!expired(current)) return current;
            var replacement = await refresh(current, ct);
            await save(replacement, CancellationToken.None); // A received rotating token must be committed even if the caller cancels.
            ct.ThrowIfCancellationRequested();
            return replacement;
        }
        finally { gate.Release(); }
    }
}
