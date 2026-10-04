using System.Buffers;
namespace GooglyWindows.Core.Protocol;

public sealed class PacketStream(Stream stream) : IAsyncDisposable
{
    public const int MaxFrameBytes = 8 * 1024 * 1024; // 60 s PCM24k WAV + base64 fits.
    private readonly SemaphoreSlim writer = new(1);
    public async Task SendAsync(Packet packet, CancellationToken ct)
    {
        var bytes = packet.Encode();
        if (bytes.Length > MaxFrameBytes) throw new InvalidDataException("Packet too large.");
        await writer.WaitAsync(ct);
        try { await stream.WriteAsync(bytes, ct); await stream.WriteAsync(new byte[] { 10 }, ct); await stream.FlushAsync(ct); }
        finally { writer.Release(); }
    }
    public async IAsyncEnumerable<Packet> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var bytes = new byte[16384];
        var frame = new ArrayBufferWriter<byte>();
        while (true)
        {
            var count = await stream.ReadAsync(bytes, ct);
            if (count == 0)
            {
                if (frame.WrittenCount != 0) throw new EndOfStreamException("Truncated packet.");
                yield break;
            }
            for (var i = 0; i < count; i++)
            {
                if (bytes[i] == 10)
                {
                    if (frame.WrittenCount != 0) yield return Packet.Decode(frame.WrittenSpan);
                    frame.Clear();
                }
                else
                {
                    if (frame.WrittenCount >= MaxFrameBytes) throw new InvalidDataException("Frame limit exceeded.");
                    frame.GetSpan(1)[0] = bytes[i]; frame.Advance(1);
                }
            }
        }
    }
    public async ValueTask DisposeAsync() { await stream.DisposeAsync(); writer.Dispose(); }
}
