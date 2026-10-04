using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace GooglyWindows;
internal static class CharacterIcon
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    internal static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new LinearGradientBrush(new Point(10, 3), new Point(24, 31), Color.FromArgb(169, 188, 255), Color.FromArgb(43, 47, 143));
            graphics.FillEllipse(fill, 1, 6, 30, 25);
            using var ink = new SolidBrush(Color.FromArgb(23, 21, 31));
            foreach (var x in new[] { 8, 19 }) { graphics.FillEllipse(Brushes.White, x, 13, 8, 8); graphics.FillEllipse(ink, x + 3, 15, 4, 4); }
            graphics.FillEllipse(ink, 14, 24, 5, 3);
        }
        var handle = bitmap.GetHicon();
        try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
}
