using System.Security.Cryptography;
using System.Text;
namespace GooglyWindows.Core.Security;

public static class Pairing
{
    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public static string Proof(string secret, string hostID, string deviceID, string nonce) =>
        Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes($"googly-v2\n{hostID}\n{deviceID}\n{nonce}")));
    public static bool Verify(string secret, string hostID, string deviceID, string nonce, string? proof)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Proof(secret, hostID, deviceID, nonce)), Convert.FromBase64String(proof ?? "")); }
        catch (FormatException) { return false; }
    }
}
