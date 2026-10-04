using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GooglyWindows.Automation;
using GooglyWindows.Core.Protocol;
using GooglyWindows.Core.Screen;
namespace GooglyWindows.Overlay;
public sealed class CursorOverlay : IDisposable
{
    private readonly List<OverlayWindow> windows = [];
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private PixelPoint from, goal, tip;
    private double started, duration;
    private bool pointing;
    private string caption = "";
    private string mood = "resting";
    private double captionExpiry = double.PositiveInfinity;
    private PixelRect? highlight;
    private double lastFace;
    public event Action<FaceState>? FaceChanged;
    public CursorOverlay()
    {
        Rebuild(); timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; timer.Tick += Tick; timer.Start();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    private void DisplayChanged(object? sender, EventArgs e) => System.Windows.Application.Current.Dispatcher.BeginInvoke(Rebuild);
    private void Rebuild()
    {
        foreach (var window in windows) window.Close(); windows.Clear();
        foreach (var monitor in Native.Monitors()) { var window = new OverlayWindow(monitor, this); windows.Add(window); window.Show(); }
        Home();
    }
    public void DragPosition(PixelPoint position)
    {
        tip = from = goal = position; started = clock.Elapsed.TotalSeconds; duration = 0; pointing = true;
        foreach (var window in windows) window.Art.InvalidateVisual();
    }
    public void Home()
    {
        var monitor = Native.Monitors().FirstOrDefault(m => m.Primary); if (monitor is null) return;
        pointing = false; highlight = null; goal = new(monitor.Bounds.Center.X, monitor.Bounds.Y + monitor.Bounds.Height - 28); from = tip; started = clock.Elapsed.TotalSeconds; duration = 0.6;
    }
    public void SetMood(string value) { mood = value; if (value == "thinking") Caption("Thinking…", false); }
    public void Caption(string text, bool done)
    {
        caption = text; captionExpiry = done ? clock.Elapsed.TotalSeconds + Math.Clamp(text.Length * 0.09 + 3, 4, 15) : double.PositiveInfinity;
    }
    public async Task PointAsync(PixelPoint target, PixelRect? rect, CancellationToken ct)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            from = tip; goal = target; highlight = rect; pointing = true; started = clock.Elapsed.TotalSeconds;
            var distance = Math.Sqrt(Math.Pow(target.X - tip.X, 2) + Math.Pow(target.Y - tip.Y, 2)); duration = Math.Clamp(0.4 + 0.12 * Math.Log2(1 + distance / 24), 0.45, 1.2); timer.Interval = TimeSpan.FromMilliseconds(16);
        });
        await Task.Delay(TimeSpan.FromSeconds(duration), ct);
    }
    private void Tick(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds; var t = Math.Clamp((now - started) / Math.Max(duration, 0.001), 0, 1); var eased = 1 - Math.Pow(1 - t, 3);
        tip = new(from.X + (goal.X - from.X) * eased, from.Y + (goal.Y - from.Y) * eased - Math.Sin(eased * Math.PI) * Math.Min(70, Math.Abs(goal.X - from.X) * 0.08));
        timer.Interval = TimeSpan.FromMilliseconds(t < 1 ? 16 : 100);
        if (now > captionExpiry) { caption = ""; if (mood == "talking") mood = "listening"; }
        foreach (var window in windows) window.Art.InvalidateVisual();
        if (now - lastFace < 0.1) return; lastFace = now;
        var monitor = Native.Monitors().FirstOrDefault(m => m.Primary); if (monitor is null) return;
        var gaze = pointing ? tip : ComputerControl.MousePosition;
        FaceChanged?.Invoke(new(Math.Clamp((gaze.X - monitor.Bounds.Center.X) / (monitor.Bounds.Width * 0.5), -1, 1),
            -Math.Clamp((monitor.Bounds.Y + monitor.Bounds.Height * 1.18 - gaze.Y) / (monitor.Bounds.Height * 1.1), 0.15, 1), pointing && mood == "listening" ? "pointing" : mood, mood == "talking" ? 0.3 + 0.3 * Math.Abs(Math.Sin(now * 12)) : 0));
    }
    public void Dispose() { timer.Stop(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; foreach (var window in windows) window.Close(); }
    private sealed class OverlayWindow : Window
    {
        public CursorArt Art { get; }
        public OverlayWindow(MonitorInfo monitor, CursorOverlay owner)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
            Width = monitor.Bounds.Width * 96 / monitor.DpiX; Height = monitor.Bounds.Height * 96 / monitor.DpiY;
            Art = new CursorArt(monitor, owner); Content = Art;
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | 0x20 | 0x80000 | 0x08000000 | 0x80);
                SetWindowPos(handle, -1, (int)monitor.Bounds.X, (int)monitor.Bounds.Y, (int)monitor.Bounds.Width, (int)monitor.Bounds.Height, 0x10);
                Screen.CapturePrivacy.Exclude(handle);
                HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) => { if (msg == 0x84) { handled = true; return -1; } return 0; });
            };
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    }
    public sealed class CursorArt(MonitorInfo monitor, CursorOverlay owner) : FrameworkElement
    {
        private static readonly LinearGradientBrush Berry = new(new GradientStopCollection { new(Color.FromRgb(169, 188, 255), 0), new(Color.FromRgb(108, 134, 245), .34), new(Color.FromRgb(66, 84, 214), .68), new(Color.FromRgb(43, 47, 143), 1) }, new(.25, .067), new(.75, .933));
        protected override void OnRender(DrawingContext dc)
        {
            var p = CoordinateMapper.ToOverlay(owner.tip, monitor); var now = owner.clock.Elapsed.TotalSeconds;
            if (owner.highlight is { } target)
            {
                var origin = CoordinateMapper.ToOverlay(new(target.X, target.Y), monitor);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(170, 108, 134, 245)), 2), new(origin.X - 3, origin.Y - 3, target.Width * 96 / monitor.DpiX + 6, target.Height * 96 / monitor.DpiY + 6), 7, 7);
            }
            if (owner.pointing || owner.mood != "resting")
            {
                var from = CoordinateMapper.ToOverlay(owner.from, monitor); var age = now - owner.started;
                if (age < owner.duration) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 108, 134, 245)), 12), new(from.X, from.Y), new(p.X + 20, p.Y + 30));
                dc.PushTransform(new TranslateTransform(p.X, p.Y));
                dc.PushTransform(new ScaleTransform(0.75, 0.75));
                var path = Geometry.Parse("M 5,0 L 36,0 C 66,0 72,18 72,36 C 72,65 54,72 36,72 C 7,72 0,54 0,36 L 0,5 Q 0,0 5,0 Z");
                dc.DrawGeometry(Berry, new Pen(Brushes.White, 3), path);
                for (var i = 0; i < 2; i++)
                {
                    var x = 31 + i * 19; var blink = now % 4.2 < 0.12;
                    dc.DrawEllipse(Brushes.White, null, new(x, 35), 7.5, blink ? 1 : 7.5);
                    if (!blink) { dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(23, 21, 31)), null, new(x - 2, 33), 3.8, 3.8); dc.DrawEllipse(Brushes.White, null, new(x - 3, 31.7), 1.3, 1.3); }
                }
                dc.Pop(); dc.Pop();
            }
            if (owner.caption.Length == 0 || !monitor.Bounds.Contains(owner.tip)) return;
            var rtl = owner.caption.Any(c => c is >= '\u0590' and <= '\u05FF');
            var text = new FormattedText(owner.caption, CultureInfo.CurrentUICulture, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 19, new SolidColorBrush(Color.FromRgb(23, 21, 31)), VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Min(480, Math.Max(150, ActualWidth - 36)), TextAlignment = TextAlignment.Center };
            var width = text.Width + 38; var height = text.Height + 28;
            var x0 = Math.Clamp(p.X - width / 2, 12, Math.Max(12, ActualWidth - width - 12)); var y0 = Math.Clamp(p.Y - height - 18, 12, Math.Max(12, ActualHeight - height - 12));
            dc.DrawRoundedRectangle(Brushes.White, new Pen(Berry, 2.5), new(x0, y0, width, height), 22, 22);
            dc.DrawText(text, new(x0 + 19, y0 + 14));
        }
    }
}
