using System.Windows;
using GooglyWindows.AppHost;
using GooglyWindows.Logging;
namespace GooglyWindows;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private bool ownsMutex;
    private CompanionRuntime? runtime;
    private System.Windows.Forms.NotifyIcon? tray;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(false, "Local\\GooglyEyes-" + Environment.UserName);
        try { ownsMutex = mutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) { System.Windows.MessageBox.Show("Googly Eyes is already running. Open it from the tray."); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { SafeLog.Event("Error", "ui", args.Exception.GetType().Name); args.Handled = true; runtime?.Report(args.Exception); };
        try
        {
            runtime = new CompanionRuntime();
            var window = new MainWindow(runtime); MainWindow = window;
            // Create the HWND even when starting in the tray, to register emergency hotkey.
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Googly Eyes", null, (_, _) => { window.Show(); window.Activate(); });
            menu.Items.Add("Stop computer control", null, (_, _) => runtime.StopActions());
            menu.Items.Add("Quit", null, async (_, _) => await QuitAsync());
            tray = new System.Windows.Forms.NotifyIcon { Icon = CharacterIcon.Create(), Text = "Googly Eyes", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += (_, _) => { window.Show(); window.Activate(); };
            if (!e.Args.Contains("--tray")) window.Show();
            await runtime.InitializeAsync();
        }
        catch (Exception error) { runtime?.Report(error); SafeLog.Event("Error", "startup", error.GetType().Name); }
    }
    private async Task QuitAsync()
    {
        tray?.Dispose(); tray = null;
        if (runtime is not null) { await runtime.DisposeAsync(); runtime = null; }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose(); if (ownsMutex) mutex?.ReleaseMutex(); mutex?.Dispose(); base.OnExit(e);
    }
}
