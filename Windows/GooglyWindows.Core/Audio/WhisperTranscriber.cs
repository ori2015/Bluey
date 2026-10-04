using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
namespace GooglyWindows.Core.Audio;
// whisper.cpp's CLI runs a multilingual ggml model locally; choose a CPU or GPU build in Settings.
public sealed class WhisperTranscriber
{
    public string Executable { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Ready => File.Exists(Executable) && File.Exists(Model);
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct)
    {
        if (!Ready) throw new InvalidOperationException("Configure the local Whisper executable and multilingual model in Settings → Transcription.");
        var pcm = To16kWav(wav);
        var dir = Path.Combine(Path.GetTempPath(), "GooglyVoice-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "input.wav"); await File.WriteAllBytesAsync(file, pcm, ct);
            var output = Path.Combine(dir, "transcript");
            var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-m", Model, "-f", file, "-l", "auto", "-nt", "-oj", "-of", output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Local Whisper couldn't start.");
            using var cancel = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct); _ = await stdout; _ = await stderr; // Never log transcript or audio.
            if (process.ExitCode != 0 || !File.Exists(output + ".json")) throw new InvalidOperationException("Local Whisper failed. Check the selected executable, multilingual model and GPU runtime.");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(output + ".json", ct));
            var text = string.Join(' ', result.RootElement.GetProperty("transcription").EnumerateArray().Select(s => s.GetProperty("text").GetString())).Trim();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("No speech was detected. Hold Bluey and try again.");
            return text;
        }
        finally { Array.Clear(pcm); try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
    public static byte[] To16kWav(byte[] wav)
    {
        if (wav.Length is < 44 or > 3_000_000 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new ArgumentException("Invalid WAV recording.");
        int rate = 0, channels = 0, bits = 0, format = 0; byte[]? samples = null;
        for (var pos = 12; pos + 8 <= wav.Length;)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(pos + 4));
            if (size > int.MaxValue || pos + 8L + size > wav.Length) throw new ArgumentException("Truncated WAV chunk.");
            var length = (int)size;
            if (wav.AsSpan(pos, 4).SequenceEqual("fmt "u8))
            {
                if (length < 16) throw new ArgumentException("Invalid WAV format.");
                format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(pos + 8)); channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(pos + 10));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(pos + 12)); bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(pos + 22));
            }
            if (wav.AsSpan(pos, 4).SequenceEqual("data"u8)) samples = wav.AsSpan(pos + 8, length).ToArray();
            pos += 8 + length + (length & 1);
        }
        if (format != 1 || channels != 1 || bits != 16 || rate != 24000 || samples is null || samples.Length % 2 != 0 || samples.Length < 480 || samples.Length > 2_880_000) throw new ArgumentException("Expected mono PCM16 WAV at 24 kHz, up to 60 seconds.");
        // Low-pass windowed-sinc resampling 24 kHz → 16 kHz; avoids aliasing from simple decimation.
        var input = new short[samples.Length / 2]; for (var i = 0; i < input.Length; i++) input[i] = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(i * 2));
        var count = input.Length * 2 / 3; var result = new byte[44 + count * 2];
        "RIFF"u8.CopyTo(result); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8); "WAVEfmt "u8.CopyTo(result.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), 16000); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), 32000); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(34), 16);
        "data"u8.CopyTo(result.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), count * 2);
        for (var i = 0; i < count; i++)
        {
            var position = i * 1.5; var center = (int)position; double sum = 0, weights = 0;
            for (var j = Math.Max(0, center - 12); j <= Math.Min(input.Length - 1, center + 12); j++)
            {
                var d = j - position; var x = d * 0.64; var sinc = Math.Abs(x) < 1e-9 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x); var w = sinc * (0.5 + 0.5 * Math.Cos(Math.PI * d / 13));
                sum += input[j] * w; weights += w;
            }
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(44 + i * 2), (short)Math.Clamp(Math.Round(sum / weights), short.MinValue, short.MaxValue));
        }
        Array.Clear(input); Array.Clear(samples); return result;
    }
}
