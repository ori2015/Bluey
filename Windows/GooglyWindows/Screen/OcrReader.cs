using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GooglyWindows.Core.Screen;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Bitmap = System.Drawing.Bitmap;
namespace GooglyWindows.Screen;
public sealed class OcrReader
{
    public string? TesseractPath { get; set; }
    public string Status { get; private set; } = "Not checked";
    public async Task<List<ScreenTarget>> ReadAsync(Bitmap image, MonitorInfo monitor, CancellationToken ct)
    {
        var found = new List<ScreenTarget>(); var available = new List<string>();
        var scale = Math.Min(1, (double)OcrEngine.MaxImageDimension / Math.Max(image.Width, image.Height));
        using var sized = new Bitmap(image, new System.Drawing.Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
        using var software = ToSoftware(sized);
        foreach (var lang in new[] { "en-US", "he-IL" })
        {
            var engine = OcrEngine.TryCreateFromLanguage(new Language(lang)); if (engine is null) continue;
            available.Add(lang);
            var result = await engine.RecognizeAsync(software).AsTask(ct);
            foreach (var line in result.Lines.Take(200))
            {
                if (line.Words.Count == 0) continue;
                var left = line.Words.Min(w => w.BoundingRect.X); var top = line.Words.Min(w => w.BoundingRect.Y);
                var right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width); var bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                PixelRect Map(double x, double y, double w, double h) => new(monitor.Bounds.X + x / scale, monitor.Bounds.Y + y / scale, w / scale, h / scale);
                found.Add(new("", line.Text, "line", Map(left, top, right - left, bottom - top)));
                foreach (var word in line.Words) { var r = word.BoundingRect; found.Add(new("", word.Text, "word", Map(r.X, r.Y, r.Width, r.Height))); }
            }
        }
        if (available.Count < 2 && !string.IsNullOrWhiteSpace(TesseractPath) && File.Exists(TesseractPath))
        {
            found.AddRange(await TesseractAsync(image, monitor, ct)); Status = "Local Tesseract eng + heb";
        }
        else Status = available.Count == 2 ? "Windows OCR: English + Hebrew" : "Missing OCR language pack: " + (available.Contains("en-US") ? "Hebrew" : "English / Hebrew") + ". Install language packs or configure Tesseract.";
        return found;
    }
    private static SoftwareBitmap ToSoftware(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        try { for (var y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4); }
        finally { bitmap.UnlockBits(data); }
        var software = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Ignore);
        using var writer = new Windows.Storage.Streams.DataWriter(); writer.WriteBytes(bytes); software.CopyFromBuffer(writer.DetachBuffer()); return software;
    }
    private async Task<List<ScreenTarget>> TesseractAsync(Bitmap image, MonitorInfo monitor, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "GooglyOCR-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "screen.png"); image.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            var start = new ProcessStartInfo(TesseractPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { file, "stdout", "-l", "eng+heb", "tsv" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Couldn't start local OCR.");
            using var registration = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct); var text = await output; _ = await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("Local OCR failed. Verify eng and heb traineddata.");
            var result = new List<ScreenTarget>();
            foreach (var line in text.Split('\n').Skip(1).Take(1200))
            {
                var p = line.Split('\t'); if (p.Length < 12 || p[0] != "5" || string.IsNullOrWhiteSpace(p[11])) continue;
                if (!double.TryParse(p[10], CultureInfo.InvariantCulture, out var confidence) || confidence < 20) continue;
                var r = new PixelRect(monitor.Bounds.X + int.Parse(p[6], CultureInfo.InvariantCulture), monitor.Bounds.Y + int.Parse(p[7], CultureInfo.InvariantCulture), int.Parse(p[8], CultureInfo.InvariantCulture), int.Parse(p[9], CultureInfo.InvariantCulture));
                result.Add(new("", p[11], "word", r));
            }
            return result;
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
