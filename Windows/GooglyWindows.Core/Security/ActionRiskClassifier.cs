using System.Text.RegularExpressions;
namespace GooglyWindows.Core.Security;
public enum ActionRisk { Routine, Confirmation, Refused }
public static partial class ActionRiskClassifier
{
    [GeneratedRegex(@"delete|remove|erase|purchase|buy|checkout|pay|send|submit|publish|password|install|uninstall|execute|run as|transfer|format|reset|מחק|מחיק|שלח|שליח|רכיש|קני|תשלום|סיסמ|התקנ|הסר", RegexOptions.IgnoreCase)]
    private static partial Regex Sensitive();
    public static ActionRisk Classify(string tool, string arguments, string target, string intent)
    {
        if (tool is "look_at_screen" or "get_active_window" or "point_at" or "point_at_spot" or "stop_pointing" or "go_to_sleep") return ActionRisk.Routine;
        if (tool == "press_keys")
        {
            var keys = Keyboard(arguments);
            if (keys.Contains((ushort)0x5B) && keys.Contains((ushort)'L') || keys.Contains((ushort)0x12) && keys.Contains((ushort)0x73)) return ActionRisk.Refused;
            if (keys.Contains((ushort)0x2E) || keys.Contains((ushort)0x0D) || keys.Contains((ushort)0x5B) || keys.Contains((ushort)0x12) && keys.Contains((ushort)0x72)) return ActionRisk.Confirmation;
        }
        if (tool == "type_text" && (arguments.Contains("\\n", StringComparison.Ordinal) || arguments.Contains("\"press_return\":true", StringComparison.OrdinalIgnoreCase))) return ActionRisk.Confirmation;
        // Coordinate-only clicks cannot be semantically classified: explicit approval.
        if (tool is "click" or "double_click" or "right_click" or "drag" && string.IsNullOrEmpty(target)) return ActionRisk.Confirmation;
        return Sensitive().IsMatch(arguments + " " + target + " " + intent) ? ActionRisk.Confirmation : ActionRisk.Routine;
    }
    private static ushort[] Keyboard(string json)
    {
        try { return Automation.KeyboardShortcut.Parse(System.Text.Json.Nodes.JsonNode.Parse(json)?["keys"]?.GetValue<string>() ?? ""); }
        catch (ArgumentException) { return []; }
    }
}
