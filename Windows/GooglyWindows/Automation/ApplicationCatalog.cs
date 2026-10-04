using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace GooglyWindows.Automation;
public sealed class ApplicationCatalog
{
    private sealed record Entry(string Name, string Path, string? Arguments = null);
    public Task<string> OpenAsync(string name, CancellationToken ct) => Task.Run(() => Open(name, ct), ct);
    private static string Open(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var wanted = name.Trim(); var running = Process.GetProcesses().Where(p =>
        {
            try { return p.MainWindowHandle != 0 && (p.ProcessName.Equals(wanted, StringComparison.OrdinalIgnoreCase) || p.MainWindowTitle.Contains(wanted, StringComparison.OrdinalIgnoreCase)); }
            catch (InvalidOperationException) { return false; }
        }).ToArray();
        try
        {
            if (running.FirstOrDefault() is { } process) { Native.ShowWindow(process.MainWindowHandle, 9); if (!Native.SetForegroundWindow(process.MainWindowHandle)) throw new InvalidOperationException("Windows couldn't focus that window."); return "Switched to " + process.ProcessName; }
        }
        finally { foreach (var process in running) process.Dispose(); }
        var entries = new List<Entry>();
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var path in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).Take(3000)) entries.Add(new(Path.GetFileNameWithoutExtension(path), path));
        }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var rootName in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" })
        {
            using var root = hive.OpenSubKey(rootName); if (root is null) continue;
            foreach (var key in root.GetSubKeyNames()) { using var sub = root.OpenSubKey(key); if (sub?.GetValue("") is string path && File.Exists(path.Trim('"'))) entries.Add(new(Path.GetFileNameWithoutExtension(key), path.Trim('"'))); }
        }
        // Windows' installed AppsFolder includes Store app launch IDs; use the shell namespace, never a shell command.
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is not null)
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                if (folder is not null)
                {
                    try { foreach (dynamic item in folder.Items()) { try { entries.Add(new((string)item.Name, "explorer.exe", "shell:AppsFolder\\" + (string)item.Path)); } finally { Marshal.ReleaseComObject(item); } } }
                    finally { Marshal.ReleaseComObject(folder); }
                }
            }
            finally { Marshal.ReleaseComObject(shell); }
        }
        ct.ThrowIfCancellationRequested();
        var app = entries.FirstOrDefault(e => e.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? entries.FirstOrDefault(e => e.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (app is null) throw new InvalidOperationException("No installed application matches that name.");
        if (app.Arguments is null) Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
        else { var start = new ProcessStartInfo(app.Path) { UseShellExecute = false }; start.ArgumentList.Add(app.Arguments); Process.Start(start); }
        return "Opened " + app.Name;
    }
    public static string OpenURL(string text)
    {
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0) throw new ArgumentException("Only http/https links are allowed.");
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); return "Opened " + url.Host;
    }
}
