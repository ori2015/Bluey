using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GooglyWindows.Core.AI;
using GooglyWindows.Core.Automation;
using GooglyWindows.Core.Screen;
using GooglyWindows.Core.Security;
using GooglyWindows.Overlay;
using GooglyWindows.Screen;
using GooglyWindows.Settings;
using GooglyWindows.Logging;
namespace GooglyWindows.Automation;
public sealed class ToolExecutor(ScreenReader screen, ComputerControl control, ApplicationCatalog apps, CursorOverlay overlay, AppSettings settings) : IToolExecutor
{
    public bool SleepRequested { get; private set; }
    public void ResetSleep() => SleepRequested = false;
    public async Task<ToolResult> ExecuteAsync(string name, string arguments, string userIntent, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        try { return await ExecuteCoreAsync(name, arguments, userIntent, ct); }
        finally { SafeLog.Event("Debug", "latency.tool", name, started.Elapsed.TotalMilliseconds); }
    }
    private async Task<ToolResult> ExecuteCoreAsync(string name, string arguments, string userIntent, CancellationToken ct)
    {
        var args = ToolCatalog.Validate(name, arguments); arguments = args.ToJsonString(); ct.ThrowIfCancellationRequested();
        string Str(string key) => args[key]?.ToString() ?? "";
        double Num(string key, double fallback = 0) => args[key]?.GetValue<double>() ?? fallback;
        if (name == "look_at_screen") return await screen.LookAsync(ct);
        if (name == "get_active_window") return new(Native.WindowTitle(Native.GetForegroundWindow()));
        if (name == "go_to_sleep") { SleepRequested = true; return new("Going to sleep after this response."); }
        if (name == "stop_pointing") { await UIAsync(overlay.Home); return new("Cursor returned home."); }
        var snapshot = screen.Snapshot;
        PixelPoint Spot(string id = "target", string x = "x", string y = "y")
        {
            if (snapshot is null || DateTimeOffset.UtcNow - snapshot.Created > TimeSpan.FromSeconds(45)) throw new ArgumentException("Call look_at_screen for fresh targets.");
            if (args[id] is not null)
            {
                var target = snapshot.Find(Str(id)); if (!target.Enabled) throw new InvalidOperationException("That control is disabled.");
                return target.Bounds.Center;
            }
            return snapshot.Coordinates.FromNormalized(Num(x), Num(y));
        }
        var targetName = args["target"] is not null ? snapshot?.Find(Str("target")).Name ?? "" : "";
        if (name == "drag" && args["from_id"] is not null) targetName = snapshot?.Find(Str("from_id")).Name ?? "";
        if (name is "point_at" or "point_at_spot") { await overlay.PointAsync(Spot(), args["target"] is not null ? snapshot?.Find(Str("target")).Bounds : null, ct); return new("Pointing there."); }
        if (!settings.ComputerControl) return new("Computer control is disabled in Settings.");
        var foreground = Native.GetForegroundWindow();
        if (name is "click" or "double_click" or "right_click" or "drag" && snapshot?.Foreground != foreground) return new("The active window changed. Call look_at_screen again.");
        if (name is "type_text" or "press_keys") { ControlsReader.RequireNonSecureFocus(); targetName = ControlsReader.FocusedControlName(); }
        if (Native.WindowTitle(foreground).Contains("PowerShell", StringComparison.OrdinalIgnoreCase) || Native.WindowTitle(foreground).Contains("Terminal", StringComparison.OrdinalIgnoreCase) || Native.WindowTitle(foreground).Contains("Command Prompt", StringComparison.OrdinalIgnoreCase)) targetName += " execute command";
        var risk = ActionRiskClassifier.Classify(name, arguments, targetName, userIntent);
        // Ambiguous intent cannot authorize a computer action solely through model inference.
        if (risk == ActionRisk.Routine && name is not "scroll" && !Regex.IsMatch(userIntent, @"open|click|type|press|scroll|drag|navigate|go to|launch|switch|search|פתח|תפתח|לחץ|תלחץ|קליד|הקלד|כתוב|גלול|גרור|תיכנס|כנס|חפש|תעבור", RegexOptions.IgnoreCase)) risk = ActionRisk.Confirmation;
        if (risk == ActionRisk.Refused) return new("That shortcut is reserved for the user.");
        if (risk == ActionRisk.Confirmation)
        {
            var action = $"{name}\nTarget: {(targetName.Length > 0 ? targetName : Native.WindowTitle(foreground))}\n{arguments}\nRequested: {userIntent}";
            if (!await ActionConfirmation.AskAsync(action, ct)) return new("The user declined confirmation. Do not retry or substitute another action.");
            ct.ThrowIfCancellationRequested();
            // Restore exactly the window reviewed by the user, never whichever app gained focus meanwhile.
            Native.SetForegroundWindow(foreground); await Task.Delay(100, ct);
            if (Native.GetForegroundWindow() != foreground) return new("The reviewed window can't be restored. Action cancelled.");
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            switch (name)
            {
                case "click": case "double_click": case "right_click":
                    var point = Spot(); await overlay.PointAsync(point, args["target"] is not null ? snapshot?.Find(Str("target")).Bounds : null, ct);
                    if (Native.GetForegroundWindow() != foreground) return new("The window changed before the click. Observe it again.");
                    if (args["target"] is not null && snapshot is not null) ControlsReader.VerifyControlTarget(snapshot.Find(Str("target")));
                    await control.ClickAsync(point, name == "double_click" ? 2 : 1, name == "right_click", ct); break;
                case "type_text": await control.TypeAsync(Str("text"), ct); if (args["press_return"]?.GetValue<bool>() == true) ComputerControl.Press("enter"); break;
                case "press_keys": ComputerControl.Press(Str("keys")); break;
                case "scroll": var location = args["target"] is not null || args["x"] is not null ? Spot() : new CoordinateMapper(Native.DesktopBounds()).FromNormalized(500, 500); ComputerControl.Scroll(location, Str("direction"), Num("amount", 3)); break;
                case "drag": var from = Spot("from_id", "from_x", "from_y"); var to = Spot("to_id", "to_x", "to_y"); await overlay.PointAsync(from, null, ct); await control.DragAsync(from, to, ct, position => UIAsync(() => overlay.DragPosition(position))); await overlay.PointAsync(to, null, ct); break;
                case "open_app": var result = await apps.OpenAsync(Str("name"), ct); screen.Invalidate(); return new(result);
                case "open_url": var url = ApplicationCatalog.OpenURL(Str("url")); screen.Invalidate(); return new(url);
                default: throw new ArgumentException("Unknown tool.");
            }
            screen.Invalidate(); return new("Action completed. Targets invalidated; call look_at_screen if verification or another target is needed.");
        }
        catch (InvalidOperationException e) { return new(e.Message); }
    }
    private static async Task UIAsync(Action action) => await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
}
