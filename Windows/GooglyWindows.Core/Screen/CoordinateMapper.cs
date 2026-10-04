namespace GooglyWindows.Core.Screen;
public readonly record struct PixelPoint(double X, double Y);
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public PixelPoint Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(PixelPoint p) => p.X >= X && p.Y >= Y && p.X < X + Width && p.Y < Y + Height;
    public bool Intersects(PixelRect r) => X < r.X + r.Width && r.X < X + Width && Y < r.Y + r.Height && r.Y < Y + Height;
}
public sealed record MonitorInfo(nint Handle, PixelRect Bounds, double DpiX, double DpiY, bool Primary);
public sealed class CoordinateMapper(PixelRect desktop)
{
    public PixelRect Desktop { get; } = desktop.Width > 0 && desktop.Height > 0 ? desktop : throw new ArgumentException("Empty desktop.");
    public PixelPoint FromNormalized(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1000 || y < 0 || y > 1000) throw new ArgumentException("Coordinates must be 0–1000.");
        return new(Desktop.X + x / 1000 * (Desktop.Width - 1), Desktop.Y + y / 1000 * (Desktop.Height - 1));
    }
    public PixelPoint ToNormalized(PixelPoint p) => new((p.X - Desktop.X) / Math.Max(1, Desktop.Width - 1) * 1000, (p.Y - Desktop.Y) / Math.Max(1, Desktop.Height - 1) * 1000);
    public static PixelPoint ToOverlay(PixelPoint p, MonitorInfo m) => new((p.X - m.Bounds.X) * 96 / m.DpiX, (p.Y - m.Bounds.Y) * 96 / m.DpiY);
    public static PixelPoint FromOverlay(PixelPoint p, MonitorInfo m) => new(m.Bounds.X + p.X * m.DpiX / 96, m.Bounds.Y + p.Y * m.DpiY / 96);
    public PixelPoint ToImage(PixelPoint p, double scale) => new((p.X - Desktop.X) * scale, (p.Y - Desktop.Y) * scale);
}
public sealed record ScreenTarget(string ID, string Name, string Kind, PixelRect Bounds,
    string AutomationID = "", bool Enabled = true, bool Focusable = false, bool Invokable = false, bool Secure = false);
public sealed class TargetSnapshot(PixelRect bounds, IEnumerable<ScreenTarget> targets, nint foreground)
{
    public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
    public nint Foreground { get; } = foreground;
    public CoordinateMapper Coordinates { get; } = new(bounds);
    public IReadOnlyDictionary<string, ScreenTarget> Targets { get; } = targets.ToDictionary(t => t.ID, StringComparer.OrdinalIgnoreCase);
    public ScreenTarget Find(string id) => Targets.TryGetValue(id, out var target) ? target : throw new ArgumentException("Unknown target; look_at_screen again.");
    public string Describe() => string.Join('\n', Targets.Values.Select(t =>
    {
        var p = Coordinates.ToNormalized(t.Bounds.Center);
        return $"{t.ID} {t.Kind} @{p.X:F0},{p.Y:F0} {System.Text.Json.JsonSerializer.Serialize(t.Secure ? "[secure field]" : t.Name)} automation_id={t.AutomationID} enabled={t.Enabled} focusable={t.Focusable} invokable={t.Invokable}";
    }));
}
