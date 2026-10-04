using System.Text.Json.Nodes;
namespace GooglyWindows.Core.Automation;
public static class ToolCatalog
{
    private static JsonObject String() => new() { ["type"] = "string" };
    private static JsonObject Number() => new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1000 };
    private static JsonObject Bool() => new() { ["type"] = "boolean" };
    private static JsonObject Target() => new() { ["target"] = String(), ["x"] = Number(), ["y"] = Number() };
    public static JsonArray Definitions(bool webSearch = false)
    {
        var functions = new JsonArray();
        void Add(string name, string description, JsonObject? properties = null, params string[] required)
        {
            functions.Add(new JsonObject { ["type"] = "function", ["name"] = name, ["description"] = description, ["strict"] = false,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties ?? new(), ["additionalProperties"] = false, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) } });
        }
        Add("look_at_screen", "Observe the virtual desktop: fresh screenshot, UI controls C#, OCR lines L# and words W#. IDs become stale after actions.");
        Add("point_at", "Fly Bluey's cursor to a target from the latest screenshot.", Target());
        Add("point_at_spot", "Point at an unlabeled object using a normalized 0–1000 screenshot coordinate.", new() { ["x"] = Number(), ["y"] = Number() }, "x", "y");
        Add("stop_pointing", "Bring the cursor home above the phone.");
        foreach (var name in new[] { "click", "double_click", "right_click" }) Add(name, "Act on a target ID; x/y only as fallback, normalized 0–1000. Requires a recent screen observation.", Target());
        Add("type_text", "Type Unicode into the focused non-password field. Embedded newlines or press_return may submit and require confirmation.", new() { ["text"] = String(), ["press_return"] = Bool() }, "text");
        Add("press_keys", "Press Windows shortcut, e.g. ctrl+t, tab, escape, F5. cmd aliases ctrl.", new() { ["keys"] = String() }, "keys");
        Add("scroll", "Scroll up/down/left/right, amount 1–10 (default 3), over a target or normalized x/y.", new() { ["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("up", "down", "left", "right") }, ["amount"] = new JsonObject { ["type"] = "number", ["minimum"] = 1, ["maximum"] = 10 }, ["target"] = String(), ["x"] = Number(), ["y"] = Number() }, "direction");
        Add("drag", "Drag from/to target IDs or normalized coordinate pairs.", new() { ["from_id"] = String(), ["to_id"] = String(), ["from_x"] = Number(), ["from_y"] = Number(), ["to_x"] = Number(), ["to_y"] = Number() });
        Add("open_app", "Open or switch to a registered installed application by name. No arbitrary executable paths.", new() { ["name"] = String() }, "name");
        Add("open_url", "Open http/https URL in the default browser.", new() { ["url"] = String() }, "url");
        Add("get_active_window", "Read the active window title and process.");
        Add("go_to_sleep", "Return Bluey to idle after a short goodbye.");
        var result = new JsonArray(new JsonObject { ["type"] = "namespace", ["name"] = "computer", ["description"] = "Bluey's local Windows tools", ["tools"] = functions });
        if (webSearch) result.Add(new JsonObject { ["type"] = "web_search" });
        return result;
    }
    public static JsonObject Validate(string name, string raw)
    {
        if (raw.Length > 32000) throw new ArgumentException("Arguments too large.");
        JsonObject args;
        try { args = JsonNode.Parse(raw)?.AsObject() ?? throw new ArgumentException("Missing object."); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { throw new ArgumentException("Invalid argument object."); }
        var definition = Definitions()[0]!["tools"]!.AsArray().FirstOrDefault(t => t?["name"]?.ToString() == name) ?? throw new ArgumentException("Unknown tool.");
        var properties = definition["parameters"]!["properties"]!.AsObject();
        foreach (var key in args.Select(p => p.Key)) if (!properties.ContainsKey(key)) throw new ArgumentException("Unknown tool argument.");
        foreach (var required in definition["parameters"]!["required"]!.AsArray()) if (args[required!.ToString()] is null) throw new ArgumentException("Missing required argument.");
        foreach (var (key, value) in args)
        {
            var schema = properties[key]!;
            var type = schema["type"]!.ToString();
            try
            {
                if (type == "string" && (value!.GetValue<string>().Length > 12000 || string.IsNullOrWhiteSpace(value.GetValue<string>()))) throw new ArgumentException("Empty or long string.");
                if (type == "boolean") _ = value!.GetValue<bool>();
                if (type == "number") { var n = value!.GetValue<double>(); if (!double.IsFinite(n) || n < schema["minimum"]!.GetValue<double>() || n > schema["maximum"]!.GetValue<double>()) throw new ArgumentException("Number out of range."); }
                if (schema["enum"] is JsonArray choices && !choices.Any(c => c!.ToString() == value!.ToString())) throw new ArgumentException("Invalid choice.");
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException) { throw new ArgumentException("Wrong argument type."); }
        }
        if (name is "click" or "double_click" or "right_click" or "point_at") ValidatePosition(args, "target", "x", "y");
        if (name == "drag") { ValidatePosition(args, "from_id", "from_x", "from_y"); ValidatePosition(args, "to_id", "to_x", "to_y"); }
        if (name == "press_keys") _ = KeyboardShortcut.Parse(args["keys"]!.ToString());
        if (name == "open_url") { var url = args["url"]!.ToString(); if (!url.Contains("://")) url = "https://" + url; if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Invalid web URL."); }
        if (name == "open_app" && args["name"]!.ToString().IndexOfAny(['/', '\\', ':']) >= 0) throw new ArgumentException("Use an installed application name.");
        return args;
    }
    private static void ValidatePosition(JsonObject args, string id, string x, string y)
    {
        if (args[id] is not null) { if (args[x] is not null || args[y] is not null) throw new ArgumentException("Choose ID or coordinates."); }
        else if (args[x] is null || args[y] is null) throw new ArgumentException("Missing target or coordinate pair.");
    }
}
