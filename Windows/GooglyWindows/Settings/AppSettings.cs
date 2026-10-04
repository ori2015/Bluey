using System.Text.Json;
using Microsoft.Win32;
using GooglyWindows.Security;
namespace GooglyWindows.Settings;
public sealed class AppSettings
{
    public bool ComputerControl { get; set; } = true;
    public bool History { get; set; } = true;
    public bool Startup { get; set; }
    public string WhisperExecutable { get; set; } = "";
    public string WhisperModel { get; set; } = "";
    public string TesseractExecutable { get; set; } = "";
    public string PreferredModel { get; set; } = "";
    public static AppSettings Load()
    {
        var path = Path.Combine(ProtectedStore.Root, "settings.json");
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(ProtectedStore.Root); var file = Path.Combine(ProtectedStore.Root, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this)); File.Move(file + ".tmp", file, true);
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (Startup) run.SetValue("GooglyEyes", "\"" + Environment.ProcessPath + "\" --tray"); else run.DeleteValue("GooglyEyes", false);
    }
}
