using System.Runtime.InteropServices;
namespace GooglyWindows.Audio;
// Records the default Windows microphone as 24 kHz mono 16-bit PCM WAV, the same format the iPhone sends.
public sealed class MicRecorder : IDisposable
{
    private const int Rate = 24000, BufferBytes = 4800, BufferCount = 6, MaxBytes = 2_800_000;
    [StructLayout(LayoutKind.Sequential)] private struct WaveFormat { public ushort Tag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, Size; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public nint Data; public uint BufferLength, BytesRecorded; public nint User; public uint Flags, Loops; public nint Next, Reserved; }
    [DllImport("winmm.dll")] private static extern int waveInOpen(out nint handle, uint device, ref WaveFormat format, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(nint handle, nint header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(nint handle, nint header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(nint handle, nint header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(nint handle);
    [DllImport("winmm.dll")] private static extern int waveInStop(nint handle);
    [DllImport("winmm.dll")] private static extern int waveInReset(nint handle);
    [DllImport("winmm.dll")] private static extern int waveInClose(nint handle);
    private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();
    private readonly MemoryStream pcm = new();
    private readonly nint[] headers = new nint[BufferCount];
    private nint handle;
    private CancellationTokenSource? polling; private Task? poller;
    public bool Recording { get; private set; }
    public event Action? LimitReached;
    public void Start()
    {
        if (Recording) return;
        var format = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSec = Rate, AvgBytesPerSec = Rate * 2, BlockAlign = 2, BitsPerSample = 16 };
        if (waveInOpen(out handle, 0xFFFFFFFF, ref format, 0, 0, 0) != 0) throw new InvalidOperationException("Couldn't open the microphone. Check Windows microphone privacy settings.");
        pcm.SetLength(0);
        for (var i = 0; i < BufferCount; i++)
        {
            var header = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(new WaveHeader { Data = Marshal.AllocHGlobal(BufferBytes), BufferLength = BufferBytes }, header, false);
            headers[i] = header;
            waveInPrepareHeader(handle, header, HeaderSize); waveInAddBuffer(handle, header, HeaderSize);
        }
        if (waveInStart(handle) != 0) { Release(); throw new InvalidOperationException("Couldn't start the microphone."); }
        Recording = true; polling = new(); var token = polling.Token;
        poller = Task.Run(async () => { while (!token.IsCancellationRequested) { Drain(true); await Task.Delay(40).ConfigureAwait(false); } });
    }
    private void Drain(bool requeue)
    {
        lock (pcm)
            foreach (var header in headers)
            {
                if (header == 0) continue;
                var h = Marshal.PtrToStructure<WaveHeader>(header);
                if ((h.Flags & 1) == 0) continue; // WHDR_DONE
                var chunk = new byte[h.BytesRecorded]; Marshal.Copy(h.Data, chunk, 0, chunk.Length); pcm.Write(chunk);
                if (requeue) { Marshal.WriteInt32(header, (int)Marshal.OffsetOf<WaveHeader>("Flags"), (int)(h.Flags & ~1u)); Marshal.WriteInt32(header, (int)Marshal.OffsetOf<WaveHeader>("BytesRecorded"), 0); waveInAddBuffer(handle, header, HeaderSize); }
                else Marshal.WriteInt32(header, (int)Marshal.OffsetOf<WaveHeader>("Flags"), (int)(h.Flags & ~1u));
                if (pcm.Length >= MaxBytes) { pcm.SetLength(MaxBytes); LimitReached?.Invoke(); }
            }
    }
    public byte[] Stop()
    {
        if (!Recording) throw new InvalidOperationException("Not recording.");
        Recording = false; polling?.Cancel(); try { poller?.Wait(); } catch (AggregateException) { }
        waveInStop(handle); waveInReset(handle); Drain(false); Release();
        var data = pcm.ToArray(); pcm.SetLength(0);
        var wav = new byte[44 + data.Length];
        "RIFF"u8.CopyTo(wav); BitConverter.TryWriteBytes(wav.AsSpan(4), wav.Length - 8); "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16); BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1); BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(24), Rate); BitConverter.TryWriteBytes(wav.AsSpan(28), Rate * 2); BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2); BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);
        "data"u8.CopyTo(wav.AsSpan(36)); BitConverter.TryWriteBytes(wav.AsSpan(40), data.Length); data.CopyTo(wav, 44);
        Array.Clear(data); return wav;
    }
    private void Release()
    {
        foreach (var (header, i) in headers.Select((h, i) => (h, i)))
        {
            if (header == 0) continue;
            waveInUnprepareHeader(handle, header, HeaderSize);
            Marshal.FreeHGlobal(Marshal.PtrToStructure<WaveHeader>(header).Data); Marshal.FreeHGlobal(header); headers[i] = 0;
        }
        if (handle != 0) { waveInClose(handle); handle = 0; }
    }
    public void Dispose() { if (Recording) { try { Stop(); } catch (InvalidOperationException) { } } pcm.Dispose(); }
}
