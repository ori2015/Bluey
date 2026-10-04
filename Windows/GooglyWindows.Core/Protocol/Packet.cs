using System.Text.Json;
using System.Text.Json.Serialization;
namespace GooglyWindows.Core.Protocol;

public sealed record FaceState(double GazeX = 0, double GazeY = 0, string Mood = "listening", double Talk = 0);
// Optional v1 fields remain wire-compatible. v2 refuses unpaired legacy clients.
public sealed record Packet
{
    public FaceState? Face { get; init; }
    public string? Hello { get; init; }
    public int? ProtocolVersion { get; init; }
    public double? Volume { get; init; }
    public string? Command { get; init; }
    public string? Audio { get; init; }
    public int? Speech { get; init; }
    public string? CallID { get; init; }
    public string? Tool { get; init; }
    public string? Text { get; init; }
    public string? Image { get; init; }
    public string? DeviceID { get; init; }
    public string? HostID { get; init; }
    public string? Secret { get; init; }
    public string? Proof { get; init; }
    public string? Nonce { get; init; }
    public string? Code { get; init; }
    public string? ErrorCode { get; init; }
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this, Json);
    public static Packet Decode(ReadOnlySpan<byte> bytes) =>
        JsonSerializer.Deserialize<Packet>(bytes, Json) ?? throw new InvalidDataException("Empty packet.");
}
