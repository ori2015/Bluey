using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GooglyWindows.AppHost;
using GooglyWindows.Automation;
using GooglyWindows.Security;
namespace GooglyWindows;
public sealed class MainWindow : Window
{
    private readonly CompanionRuntime runtime;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock phone = new();
    private readonly TextBlock account = new();
    private readonly TextBlock model = new();
    private readonly TextBlock transcription = new();
    private readonly TextBlock control = new();
    private readonly StackPanel settings = new();
    public MainWindow(CompanionRuntime runtime)
    {
        this.runtime = runtime; Title = "Googly Eyes"; Width = 470; Height = 650; MinWidth = 420; MinHeight = 550;
        Background = new SolidColorBrush(Color.FromRgb(30, 27, 41)); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new Thickness(26) };
        root.Children.Add(new TextBlock { Text = "🫐  Googly Eyes", FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) });
        var card = new StackPanel();
        foreach (var row in new[] { phone, account, model, transcription, control }) { row.Margin = new Thickness(0, 5, 0, 5); card.Children.Add(row); }
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(42, 38, 64)), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 0, 12), Child = card });
        status.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 171)); status.Margin = new Thickness(2, 0, 2, 12);
        root.Children.Add(status);
        root.Children.Add(Button("Settings", () => { ShowSettings(); return Task.CompletedTask; }));
        root.Children.Add(Button("Reconnect iPhone", () => { runtime.Phone?.ReconnectAll(); return Task.CompletedTask; }));
        root.Children.Add(Button("Disconnect ChatGPT", SignOutAsync, true));
        root.Children.Add(new TextBlock { Text = "Hold Ctrl + Alt + Space to talk to Bluey  ·  Ctrl + Alt + S stops computer control", FontSize = 12, Foreground = Brushes.LightGray });
        root.Children.Add(settings); Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Keep the settings panel visible in screenshots for support and troubleshooting.
            var source = HwndSource.FromHwnd(handle);
            source?.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) => { if (msg == 0x0312) { if (w == 2) runtime.BeginKeyboardVoice(); else { runtime.StopActions(); Refresh(); } handled = true; } return 0; });
            if (!Native.RegisterHotKey(handle, 1, 0x4003, 'S')) status.Text = "Emergency shortcut unavailable; use Stop in the tray menu.";
            if (!Native.RegisterHotKey(handle, 2, 0x4003, 0x20)) status.Text = "Voice shortcut Ctrl + Alt + Space is used by another app.";
        };
        runtime.Changed += Refresh; Refresh();
    }
    private void Refresh()
    {
        phone.Text = "iPhone: " + (runtime.Phone?.Names is { Length: > 0 } names ? string.Join(", ", names) : "Disconnected");
        account.Text = "ChatGPT: " + (runtime.Auth.Account?.Sharing == true ? "Connected as " + runtime.Auth.Account.Email : "Not connected for plan usage");
        model.Text = "AI model: " + (runtime.Models.Active?.DisplayName ?? "Not validated");
        transcription.Text = "Transcription: Local Whisper" + (runtime.Whisper.Ready ? " ✓" : " — setup required");
        control.Text = "Computer control: " + (runtime.Settings.ComputerControl ? "Enabled" : "Disabled"); status.Text = runtime.Status;
    }
    private Button Button(string title, Func<Task> action, bool secondary = false)
    {
        var button = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (secondary) button.Background = new SolidColorBrush(Color.FromRgb(68, 63, 102));
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception e) { runtime.Report(e); }
            finally { button.IsEnabled = true; Refresh(); }
        };
        return button;
    }
    private async Task SignOutAsync()
    {
        runtime.CancelRequests(); runtime.Models.Clear();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(runtime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var confirmed = await runtime.Auth.SignOutAsync(timeout.Token);
        if (!confirmed) System.Windows.MessageBox.Show("Signed out locally. Remote revocation wasn't confirmed. Disconnect Googly Eyes in ChatGPT Settings.", "ChatGPT sign-out");
    }
    private void ShowSettings()
    {
        settings.Children.Clear();
        var tabs = new TabControl { Margin = new Thickness(0, 18, 0, 0) };
        StackPanel Tab(string title) { var p = new StackPanel { Margin = new Thickness(12) }; tabs.Items.Add(new TabItem { Header = title, Content = p }); return p; }
        var chat = Tab("ChatGPT");
        chat.Children.Add(new TextBlock { Text = runtime.Auth.Account?.Email ?? "Use your eligible ChatGPT plan", TextWrapping = TextWrapping.Wrap });
        chat.Children.Add(new TextBlock { Text = runtime.Auth.Account?.Sharing == true ? "Connected\nChatGPT plan usage enabled" : "Sign in and grant ChatGPT plan usage." });
        chat.Children.Add(Button(runtime.Auth.Account?.Sharing == true ? "Reconnect" : "Continue with ChatGPT", async () => { runtime.CancelRequests(); await runtime.Auth.SignInAsync(runtime.Token); runtime.Responses.ResumeUsage(); await runtime.LoadModelsAsync(runtime.Token); ShowSettings(); }));
        chat.Children.Add(Button("Manage usage", () => { ApplicationCatalog.OpenURL("https://chatgpt.com/settings/usage"); return Task.CompletedTask; }));
        chat.Children.Add(Button("Sign out", async () => { await SignOutAsync(); ShowSettings(); }, true));
        chat.Children.Add(Button("Resume plan requests", () => { runtime.Responses.ResumeUsage(); return Task.CompletedTask; }));
        chat.Children.Add(new TextBlock { Text = "Active model (completed inference required)" });
        var choices = new ComboBox { ItemsSource = runtime.Models.Models, SelectedItem = runtime.Models.Active, Margin = new Thickness(0, 5, 0, 8) }; chat.Children.Add(choices);
        async Task ChooseAsync()
        {
            if (choices.SelectedItem is not Models.ModelChoice selected) return;
            await runtime.Models.ValidateAsync(selected, runtime.Token); runtime.Settings.PreferredModel = selected.Slug; runtime.Settings.Save(); runtime.Report(new InvalidOperationException("Model " + selected.DisplayName + " saved for next time.")); ShowSettings();
        }
        // Picking a model validates it and remembers it for the next launch.
        choices.SelectionChanged += async (_, e) => { if (e.AddedItems.Count == 0 || !choices.IsDropDownOpen && !choices.IsKeyboardFocusWithin) return; try { await ChooseAsync(); } catch (Exception ex) { runtime.Report(ex); Refresh(); } };
        chat.Children.Add(Button("Validate selected model", ChooseAsync));
        chat.Children.Add(Button("Test web research", async () => { await runtime.Models.ProbeWebAsync(runtime.Token); ShowSettings(); }));
        chat.Children.Add(new TextBlock { Text = runtime.Models.WebStatus, TextWrapping = TextWrapping.Wrap });
        chat.Children.Add(new TextBlock { Text = "AI: " + (runtime.Models.Active is null ? "Not validated" : "ChatGPT Plan ✓") + "\nTranscription: Local fallback (subscription audio unsupported)", TextWrapping = TextWrapping.Wrap });
        var pairing = Tab("iPhone");
        pairing.Children.Add(new TextBlock { Text = "Pair your iPhone", FontSize = 20 });
        pairing.Children.Add(new TextBlock { Text = "Generate a code, then compare this full certificate fingerprint with your iPhone before entering the code.", TextWrapping = TextWrapping.Wrap });
        pairing.Children.Add(new TextBox { Text = runtime.Fingerprint, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") });
        var code = new TextBlock { FontSize = 30, Text = runtime.Pairing.Code ?? "—" }; pairing.Children.Add(code);
        pairing.Children.Add(Button("Generate pairing code", () => { code.Text = runtime.Pairing.BeginPairing(); return Task.CompletedTask; }));
        pairing.Children.Add(new TextBlock { Text = "Code expires after two minutes; five attempts maximum." });
        pairing.Children.Add(Button("Forget paired devices", async () => { await runtime.Pairing.ForgetAllAsync(runtime.Token); runtime.Phone?.ReconnectAll(); }));
        var audio = Tab("Local AI");
        audio.Children.Add(new TextBlock { Text = "Transcription source: Local Whisper", FontSize = 18 });
        audio.Children.Add(new TextBlock { Text = "Select a whisper.cpp CPU or GPU executable and a multilingual ggml model (without .en). Speech is always transcribed as Hebrew.", TextWrapping = TextWrapping.Wrap });
        var exe = new TextBox { Text = runtime.Settings.WhisperExecutable }; audio.Children.Add(exe);
        audio.Children.Add(Button("Choose whisper-cli.exe", () => PickAsync(exe, "Whisper executable|*.exe")));
        var whisperModel = new TextBox { Text = runtime.Settings.WhisperModel }; audio.Children.Add(whisperModel);
        audio.Children.Add(Button("Choose multilingual model", () => PickAsync(whisperModel, "Whisper model|*.bin")));
        audio.Children.Add(new TextBlock { Text = "Optional local Tesseract OCR (eng + heb)" });
        var tess = new TextBox { Text = runtime.Settings.TesseractExecutable }; audio.Children.Add(tess);
        audio.Children.Add(Button("Choose tesseract.exe", () => PickAsync(tess, "Tesseract executable|*.exe")));
        audio.Children.Add(Button("Save local AI settings", () => { runtime.Settings.WhisperExecutable = exe.Text; runtime.Settings.WhisperModel = whisperModel.Text; runtime.Settings.TesseractExecutable = tess.Text; runtime.Settings.Save(); runtime.ApplySettings(); return Task.CompletedTask; }));
        var preferences = Tab("Privacy");
        CheckBox Toggle(string title, bool value, Action<bool> update)
        {
            var toggle = new CheckBox { Content = title, IsChecked = value }; toggle.Click += (_, _) => { update(toggle.IsChecked == true); runtime.Settings.Save(); Refresh(); }; preferences.Children.Add(toggle); return toggle;
        }
        Toggle("Let Bluey use the computer", runtime.Settings.ComputerControl, v => { runtime.Settings.ComputerControl = v; if (!v) runtime.CancelRequests(); });
        Toggle("Start Googly Eyes with Windows", runtime.Settings.Startup, v => runtime.Settings.Startup = v);
        Toggle("Save conversation history locally", runtime.Settings.History, v => runtime.Settings.History = v);
        preferences.Children.Add(Button("Delete saved Windows history", () => { runtime.History.DeleteAll(); return Task.CompletedTask; }));
        preferences.Children.Add(new TextBlock { Text = "Audio and screenshots aren't saved as history. Temporary local audio/OCR files are deleted after processing. ChatGPT credentials stay on this computer.", TextWrapping = TextWrapping.Wrap });
        preferences.Children.Add(Button("Open logs", () => { var dir = Path.Combine(ProtectedStore.Root, "Logs"); Directory.CreateDirectory(dir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true }); return Task.CompletedTask; }));
        preferences.Children.Add(new TextBlock { Text = "About: Bluey by Riley · Windows port\nPublic Responses API, ChatGPT plan usage. No API-key billing fallback.\nSee BUILD.md and TROUBLESHOOTING.md.", TextWrapping = TextWrapping.Wrap });
        settings.Children.Add(tabs);
    }
    private static Task PickAsync(TextBox box, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter }; if (dialog.ShowDialog() == true) box.Text = dialog.FileName; return Task.CompletedTask;
    }
}
