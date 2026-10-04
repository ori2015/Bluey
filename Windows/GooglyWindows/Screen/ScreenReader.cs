using System.Text;
using GooglyWindows.Automation;
using GooglyWindows.Core.AI;
using GooglyWindows.Core.Screen;
using Bitmap = System.Drawing.Bitmap;
namespace GooglyWindows.Screen;
public sealed class ScreenReader(GraphicsCapture capture, ControlsReader controls, OcrReader ocr)
{
    public TargetSnapshot? Snapshot { get; private set; }
    public void Invalidate() => Snapshot = null;
    public async Task<ToolResult> LookAsync(CancellationToken ct)
    {
        CapturePrivacy.RequireExcludedWindows();
        var monitors = Native.Monitors(); var bounds = Native.DesktopBounds(); var foreground = Native.GetForegroundWindow();
        var targets = await controls.ReadAsync(foreground, bounds, ct);
        using var desktop = new Bitmap((int)bounds.Width, (int)bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(desktop))
        {
            graphics.Clear(System.Drawing.Color.Black);
            foreach (var monitor in monitors)
            {
                ct.ThrowIfCancellationRequested();
                using var bitmap = await capture.CaptureAsync(monitor, ct);
                using (var redact = System.Drawing.Graphics.FromImage(bitmap))
                    foreach (var secure in targets.Where(t => t.Secure && t.Bounds.Intersects(monitor.Bounds)))
                        redact.FillRectangle(System.Drawing.Brushes.Black, (float)(secure.Bounds.X - monitor.Bounds.X), (float)(secure.Bounds.Y - monitor.Bounds.Y), (float)secure.Bounds.Width, (float)secure.Bounds.Height);
                targets.AddRange(await ocr.ReadAsync(bitmap, monitor, ct));
                graphics.DrawImage(bitmap, (int)(monitor.Bounds.X - bounds.X), (int)(monitor.Bounds.Y - bounds.Y), (int)monitor.Bounds.Width, (int)monitor.Bounds.Height);
            }
        }
        var lines = 0; var words = 0;
        targets = targets.Take(1500).Select(t => t.Kind == "line" ? t with { ID = "L" + ++lines } : t.Kind == "word" ? t with { ID = "W" + ++words } : t).ToList();
        Snapshot = new(bounds, targets, foreground);
        var scale = Math.Min(1, 1600d / Math.Max(desktop.Width, desktop.Height));
        using var small = new Bitmap(Math.Max(1, (int)(desktop.Width * scale)), Math.Max(1, (int)(desktop.Height * scale)));
        using (var graphics = System.Drawing.Graphics.FromImage(small)) { graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; graphics.DrawImage(desktop, 0, 0, small.Width, small.Height); }
        using var bytes = new MemoryStream(); small.Save(bytes, System.Drawing.Imaging.ImageFormat.Jpeg);
        var mouse = Snapshot.Coordinates.ToNormalized(ComputerControl.MousePosition);
        var description = new StringBuilder($"Windows active window: {Native.WindowTitle(foreground)}\nMouse @{mouse.X:F0},{mouse.Y:F0}\nVirtual desktop physical bounds: {bounds}\nNormalized screenshot coordinates 0–1000.\n{ocr.Status}\n");
        description.Append(Snapshot.Describe());
        return new(description.ToString(), Convert.ToBase64String(bytes.ToArray()));
    }
}
