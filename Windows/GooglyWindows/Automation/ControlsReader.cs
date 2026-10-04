using System.Diagnostics;
using System.Windows.Automation;
using GooglyWindows.Core.Screen;
namespace GooglyWindows.Automation;
public sealed class ControlsReader
{
    private readonly SemaphoreSlim gate = new(1);
    // Providers can block native UIA calls. One isolated worker at a time; timeout prevents accumulating stuck workers.
    public async Task<List<ScreenTarget>> ReadAsync(nint foreground, PixelRect desktop, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) return [];
        var work = Task.Run(() =>
        {
            try { return Read(foreground, desktop, ct); } finally { gate.Release(); }
        }, CancellationToken.None);
        try { return await work.WaitAsync(TimeSpan.FromSeconds(3), ct); }
        catch (TimeoutException) { return []; }
    }
    private static List<ScreenTarget> Read(nint foreground, PixelRect desktop, CancellationToken ct)
    {
        var targets = new List<ScreenTarget>(); var time = Stopwatch.StartNew();
        var root = AutomationElement.FromHandle(foreground); var queue = new Queue<AutomationElement>(); queue.Enqueue(root); var visited = 0;
        while (queue.Count > 0 && visited++ < 2000 && targets.Count < 250 && time.ElapsedMilliseconds < 900)
        {
            ct.ThrowIfCancellationRequested();
            var element = queue.Dequeue();
            try
            {
                var p = element.Current; var r = p.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 2 && r.Height > 2 && !p.IsOffscreen && desktop.Intersects(new(r.X, r.Y, r.Width, r.Height)))
                    targets.Add(new("C" + (targets.Count + 1), p.IsPassword ? "[secure field]" : p.Name, p.ControlType.ProgrammaticName.Replace("ControlType.", ""), new(r.X, r.Y, r.Width, r.Height), p.AutomationId, p.IsEnabled, p.IsKeyboardFocusable, element.TryGetCurrentPattern(InvokePattern.Pattern, out _) || element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _), p.IsPassword));
                if (p.IsPassword) continue;
                var walker = TreeWalker.ControlViewWalker; var child = walker.GetFirstChild(element); var children = 0;
                while (child is not null && children++ < 150) { queue.Enqueue(child); child = walker.GetNextSibling(child); }
            }
            catch (Exception e) when (e is ElementNotAvailableException or System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        }
        return targets;
    }
    public static string FocusedControlName()
    {
        try { var focused = AutomationElement.FocusedElement; return focused?.Current.IsPassword == true ? "[secure field]" : focused?.Current.Name ?? ""; }
        catch (Exception e) when (e is ElementNotAvailableException or System.Runtime.InteropServices.COMException) { return ""; }
    }
    public static void VerifyControlTarget(ScreenTarget target)
    {
        if (!target.ID.StartsWith('C')) return;
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(target.Bounds.Center.X, target.Bounds.Center.Y));
            for (var i = 0; element is not null && i < 8; i++, element = TreeWalker.ControlViewWalker.GetParent(element))
            {
                var current = element.Current; var rect = current.BoundingRectangle;
                if (current.AutomationId == target.AutomationID && current.Name == target.Name && !current.IsOffscreen && current.IsEnabled && Math.Abs(rect.X - target.Bounds.X) < 3 && Math.Abs(rect.Y - target.Bounds.Y) < 3) return;
            }
        }
        catch (Exception e) when (e is ElementNotAvailableException or System.Runtime.InteropServices.COMException) { }
        throw new InvalidOperationException("The target control changed. Look at the screen again before clicking.");
    }
    public static void RequireNonSecureFocus()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || element.Current.IsPassword) throw new InvalidOperationException("Type passwords yourself. Bluey can't inspect this field safely.");
        }
        catch (ElementNotAvailableException) { throw new InvalidOperationException("The focused field is unavailable. Click a field first."); }
    }
}
