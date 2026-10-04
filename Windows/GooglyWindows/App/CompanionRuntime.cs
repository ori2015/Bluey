using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using GooglyWindows.AI;
using GooglyWindows.Core.Audio;
using GooglyWindows.Auth;
using GooglyWindows.Automation;
using GooglyWindows.Core.AI;
using GooglyWindows.Core.Automation;
using GooglyWindows.Core.Protocol;
using GooglyWindows.Logging;
using GooglyWindows.Models;
using GooglyWindows.Networking;
using GooglyWindows.Overlay;
using GooglyWindows.Screen;
using GooglyWindows.Security;
using GooglyWindows.Settings;
namespace GooglyWindows.AppHost;
public sealed class CompanionRuntime : IAsyncDisposable
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public ProtectedStore Store { get; } = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(180) };
    public ChatGptAuth Auth { get; }
    public ResponsesClient Responses { get; }
    public ModelCatalog Models { get; }
    public PairingRegistry Pairing { get; }
    public WhisperTranscriber Whisper { get; } = new();
    public OcrReader Ocr { get; } = new();
    public CursorOverlay Overlay { get; }
    public PhoneServer? Phone { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Status { get; private set; } = "Starting…";
    public SessionHistory History { get; }
    public event Action? Changed;
    private readonly ToolExecutor executor;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<string, (CancellationTokenSource Cancel, Task Task)> requests = new();
    private readonly ConcurrentDictionary<string, ConversationState> conversations = new();
    private readonly SemaphoreSlim operation = new(1);
    private MdnsAdvertiser? mdns;
    public CompanionRuntime()
    {
        Auth = new(http, Store); Responses = new(http, Auth); Models = new(Responses); Pairing = new(Store);
        Overlay = new(); History = new(Settings);
        executor = new(new ScreenReader(new(), new(), Ocr), new(), new(), Overlay, Settings);
        ApplySettings();
        Auth.Changed += Notify;
        Overlay.FaceChanged += face => { if (Phone is { } server) { server.LastFace = face; _ = BroadcastFaceAsync(server, face); } };
    }
    public CancellationToken Token => stopping.Token;
    private async Task BroadcastFaceAsync(PhoneServer server, FaceState face)
    {
        try { await server.BroadcastAsync(new Packet { Face = face, Command = "face" }, Token); }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
    public async Task InitializeAsync()
    {
        foreach (var prefix in new[] { "GooglyVoice-", "GooglyOCR-" })
            foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), prefix + "*"))
                try { Directory.Delete(directory, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        await Auth.InitializeAsync(Token); await Pairing.LoadAsync(Token);
        var certificate = await HostCertificate.LoadAsync(Store, Token); Fingerprint = HostCertificate.Fingerprint(certificate);
        Phone = new(Pairing, certificate, await Auth.HostIDAsync(Token)); Phone.Changed += Notify; Phone.OnPacket = HandleAsync; Phone.Start();
        mdns = new(Phone.Port, await Auth.HostIDAsync(Token)); mdns.Start();
        SetStatus("Ready");
        if (Auth.Account?.Sharing == true)
        {
            try { await LoadModelsAsync(Token); }
            catch (Exception e) { Report(e); }
        }
    }
    public void ApplySettings() { Whisper.Executable = Settings.WhisperExecutable; Whisper.Model = Settings.WhisperModel; Ocr.TesseractPath = Settings.TesseractExecutable; }
    public async Task LoadModelsAsync(CancellationToken ct)
    {
        await Models.DiscoverAsync(ct);
        var choices = Models.Models.OrderByDescending(m => m.Slug == Settings.PreferredModel).ToArray();
        foreach (var model in choices.Take(4))
        {
            try { await Models.ValidateAsync(model, ct); SetStatus("ChatGPT plan usage validated"); return; }
            catch (SubscriptionException e) when (e.IsCapability) { }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("No model validated text, vision and function calls. Select another model in Settings.");
    }
    private Task HandleAsync(PhonePeer peer, Packet packet, CancellationToken ct)
    {
        var key = peer.DeviceID + ":" + packet.CallID;
        if (packet.Command == "cancel_request") { if (requests.TryGetValue(key, out var request)) CancelSafely(request.Cancel); return Task.CompletedTask; }
        if (packet.Command == "asleep")
        {
            foreach (var pending in requests.Where(p => p.Key.StartsWith(peer.DeviceID + ":", StringComparison.Ordinal))) CancelSafely(pending.Value.Cancel);
            conversations.TryRemove(peer.DeviceID, out _); _ = UIAsync(() => { Overlay.Home(); Overlay.SetMood("resting"); }); return Task.CompletedTask;
        }
        if (packet.Command == "awake") { _ = UIAsync(() => Overlay.SetMood("listening")); return Task.CompletedTask; }
        if (packet.Command is not ("voice_request" or "text_request")) return Task.CompletedTask;
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct, Token); cancel.CancelAfter(TimeSpan.FromMinutes(4));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!requests.TryAdd(key, (cancel, completion.Task))) { cancel.Dispose(); return Task.CompletedTask; }
        _ = RunTrackedAsync(); return Task.CompletedTask;
        async Task RunTrackedAsync()
        {
            try { await TurnAsync(peer, packet, cancel.Token); }
            finally { requests.TryRemove(key, out _); cancel.Dispose(); completion.TrySetResult(); }
        }
    }
    private async Task TurnAsync(PhonePeer peer, Packet packet, CancellationToken ct)
    {
        var acquired = false;
        try
        {
            acquired = await operation.WaitAsync(0, ct);
            if (!acquired) throw new InvalidOperationException("Bluey is finishing another request. Hold to ask again shortly.");
            var model = Models.Active ?? throw new InvalidOperationException("Connect ChatGPT and validate a model in Windows Settings first.");
            if (Responses.UsagePaused) throw new SubscriptionException("subscription_sharing_usage_limit_exceeded");
            await UIAsync(() => { Overlay.Caption("", false); Overlay.SetMood("thinking"); }); executor.ResetSleep();
            var timing = Stopwatch.StartNew(); string user;
            if (packet.Command == "voice_request")
            {
                byte[] audio;
                try { audio = Convert.FromBase64String(packet.Audio ?? ""); } catch (FormatException) { throw new ArgumentException("Invalid voice recording."); }
                try { user = await Whisper.TranscribeAsync(audio, ct); } finally { Array.Clear(audio); }
                SafeLog.Event("Debug", "latency", "received_to_transcript", timing.Elapsed.TotalMilliseconds);
            }
            else user = packet.Text is { Length: > 0 and <= 12000 } text ? text : throw new ArgumentException("Invalid text request.");
            await peer.SendAsync(new Packet { Command = "transcript", CallID = packet.CallID, Text = user }, ct);
            await History.AddAsync("user", user, ct); timing.Restart(); var first = true; var caption = "";
            var conversation = conversations.GetOrAdd(peer.DeviceID, _ => new());
            var loop = new AgentLoop(Responses, executor, conversation);
            var output = await loop.RunAsync(model.Slug, user, ToolCatalog.Definitions(model.Capabilities?.WebSearch == true), async delta =>
            {
                if (first) { SafeLog.Event("Debug", "latency", "transcript_to_first_delta", timing.Elapsed.TotalMilliseconds); first = false; }
                caption += delta;
                await peer.SendAsync(new Packet { Command = "assistant_delta", CallID = packet.CallID, Text = delta }, ct);
                await UIAsync(() => { Overlay.SetMood("talking"); Overlay.Caption(caption, false); });
            }, async (name, id, started) =>
            {
                await peer.SendAsync(new Packet { Command = started ? "tool_started" : "tool_done", CallID = packet.CallID, Tool = name, Text = id }, ct);
                await History.AddAsync(started ? "tool_started" : "tool_done", name, ct);
            }, ct, (phase, ms) => SafeLog.Event("Debug", "latency", phase, ms),
                (name, result) => History.AddAsync("tool_outcome", name + ": " + (name == "look_at_screen" ? "Screen observation returned" : result.Text[..Math.Min(result.Text.Length, 300)]), ct));
            await History.AddAsync("assistant", output, ct);
            await peer.SendAsync(new Packet { Command = "assistant_done", CallID = packet.CallID, Text = output }, ct);
            await UIAsync(() => { Overlay.Caption(output, true); Overlay.SetMood("listening"); });
            if (executor.SleepRequested) { await peer.SendAsync(new Packet { Command = "sleep" }, ct); await UIAsync(() => { Overlay.Home(); Overlay.SetMood("resting"); }); }
            SetStatus("Ready");
        }
        catch (Exception error)
        {
            Report(error);
            await UIAsync(() => { Overlay.Caption(ErrorText(error), true); Overlay.SetMood("listening"); });
            if (error is SubscriptionException ex && ex.IsCapability && ex.Parameter?.Contains("web", StringComparison.OrdinalIgnoreCase) == true) Models.DisableWeb();
            try { await peer.SendAsync(new Packet { Command = "assistant_error", CallID = packet.CallID, Text = ErrorText(error), ErrorCode = error is SubscriptionException sub ? sub.Code : "request_failed" }, Token); }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        finally { if (acquired) { operation.Release(); await UIAsync(() => Overlay.SetMood("listening")); } }
    }
    private static void CancelSafely(CancellationTokenSource source) { try { source.Cancel(); } catch (ObjectDisposedException) { } }
    public void StopActions() { Settings.ComputerControl = false; foreach (var r in requests.Values) CancelSafely(r.Cancel); SetStatus("Stopped. Computer control is disabled."); }
    public void CancelRequests() { foreach (var r in requests.Values) CancelSafely(r.Cancel); }
    public void Report(Exception error) { SetStatus(ErrorText(error)); SafeLog.Event("Warning", "runtime", error is SubscriptionException sub ? sub.Code : error.GetType().Name); }
    private static string ErrorText(Exception e) => e switch { SubscriptionException => e.Message, InvalidOperationException => e.Message, ArgumentException => "Invalid request or recording.", OperationCanceledException => "Request cancelled or timed out.", _ => "Couldn't complete the request. Check Settings and the connection." };
    private void SetStatus(string status) { Status = status; Notify(); }
    private void Notify() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Changed?.Invoke());
    private static async Task UIAsync(Action action) => await System.Windows.Application.Current.Dispatcher.InvokeAsync(action);
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); CancelRequests();
        if (mdns is not null) await mdns.DisposeAsync();
        if (Phone is not null) await Phone.DisposeAsync();
        await Task.WhenAll(requests.Values.Select(r => r.Task)); Overlay.Dispose(); http.Dispose(); stopping.Dispose();
    }
}
