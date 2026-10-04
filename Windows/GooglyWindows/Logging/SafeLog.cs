using System.Text.Json;
using System.Text.RegularExpressions;
using GooglyWindows.Security;
namespace GooglyWindows.Logging;
public static partial class SafeLog
{
    private static readonly object Gate = new();
    [GeneratedRegex(@"(?i)(bearer\s+\S+|(?:access_token|refresh_token|id_token|password|secret|audio|image)\s*[:=]\s*[^\s,]+|sk-[\w-]+|eyJ[\w-]+\.[\w-]+\.[\w-]+)")]
    private static partial Regex Secrets();
    public static string Redact(string text) => Secrets().Replace(text, "[redacted]");
    // Only categorical events and numeric timings enter logging. Never pass request/exception bodies.
    public static void Event(string level, string category, string code, double? milliseconds = null)
    {
#if !DEBUG
        if (level == "Debug") return;
#endif
        lock (Gate)
        {
            var dir = Path.Combine(ProtectedStore.Root, "Logs"); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
            var line = JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, level, category = Redact(category), code = Redact(code), milliseconds });
            try { File.AppendAllText(file, line + Environment.NewLine); } catch (IOException) { }
        }
    }
}
