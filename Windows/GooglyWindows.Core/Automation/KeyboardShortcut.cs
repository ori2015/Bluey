namespace GooglyWindows.Core.Automation;
public static class KeyboardShortcut
{
    private static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"]=0x11,["control"]=0x11,["cmd"]=0x11,["command"]=0x11,["alt"]=0x12,["option"]=0x12,["shift"]=0x10,["win"]=0x5B,["meta"]=0x5B,
        ["enter"]=0x0D,["return"]=0x0D,["tab"]=9,["escape"]=0x1B,["esc"]=0x1B,["space"]=0x20,["delete"]=0x2E,["backspace"]=8,
        ["left"]=0x25,["up"]=0x26,["right"]=0x27,["down"]=0x28,["home"]=0x24,["end"]=0x23,["pageup"]=0x21,["pagedown"]=0x22
    };
    public static ushort[] Parse(string combo)
    {
        var parts = combo.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 5 || parts.Any(string.IsNullOrEmpty)) throw new ArgumentException("Invalid shortcut.");
        var result = parts.Select(p => Keys.TryGetValue(p, out var code) ? code :
            p.Length == 1 && char.IsAsciiLetterOrDigit(p[0]) ? (ushort)char.ToUpperInvariant(p[0]) :
            p.StartsWith('f') && int.TryParse(p.AsSpan(1), out var f) && f is >= 1 and <= 12 ? (ushort)(0x6F + f) :
            throw new ArgumentException("Unknown keyboard key.")).ToArray();
        if (result.Distinct().Count() != result.Length || result.Take(result.Length - 1).Any(k => k is not (0x11 or 0x12 or 0x10 or 0x5B)) || result[^1] is 0x11 or 0x12 or 0x10 or 0x5B)
            throw new ArgumentException("Shortcut needs modifiers followed by one key.");
        return result;
    }
}
