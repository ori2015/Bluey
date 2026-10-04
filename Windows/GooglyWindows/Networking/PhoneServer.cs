using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using GooglyWindows.Core.Protocol;
using GooglyWindows.Core.Auth;
using GooglyWindows.Logging;
namespace GooglyWindows.Networking;
public sealed class PhonePeer(PacketStream? wire, string deviceID, string name)
{
    public string DeviceID { get; } = deviceID;
    public string Name { get; } = name;
    public Task SendAsync(Packet packet, CancellationToken ct) => wire?.SendAsync(packet, ct) ?? Task.CompletedTask; // null wire: the on-PC keyboard session
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class PhoneServer(PairingRegistry pairing, X509Certificate2 certificate, string hostID) : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Any, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<string, (PhonePeer Peer, TcpClient Client)> peers = new();
    private readonly ConcurrentDictionary<Task, byte> clients = new();
    private readonly SemaphoreSlim capacity = new(8);
    private Task? accepting;
    public Func<PhonePeer, Packet, CancellationToken, Task>? OnPacket { get; set; }
    public event Action? Changed;
    public FaceState LastFace { get; set; } = new();
    public string[] Names => peers.Values.Select(p => p.Peer.Name).ToArray();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void Start() { listener.Start(8); accepting = AcceptAsync(stop.Token); }
    private async Task AcceptAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                if (!capacity.Wait(0)) { client.Dispose(); continue; }
                var task = ServeAsync(client, ct); clients.TryAdd(task, 0);
                _ = ObserveAsync(task);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException && ct.IsCancellationRequested) { }
    }
    private async Task ObserveAsync(Task task)
    {
        try { await task; } catch (Exception) { SafeLog.Event("Warning", "phone", "client_failure"); }
        finally { clients.TryRemove(task, out _); capacity.Release(); }
    }
    private async Task ServeAsync(TcpClient client, CancellationToken serverCT)
    {
        using (client)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(serverCT))
        {
            var ct = lifetime.Token;
            client.NoDelay = true; client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            using var tls = new SslStream(client.GetStream(), false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct); handshake.CancelAfter(TimeSpan.FromSeconds(12));
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, ClientCertificateRequired = false }, handshake.Token);
            await using var wire = new PacketStream(tls);
            var nonce = OAuthPrimitives.RandomValue();
            await wire.SendAsync(new Packet { Hello = Environment.MachineName, ProtocolVersion = 2, Command = "pair_challenge", Nonce = nonce, HostID = hostID }, ct);
            PhonePeer? peer = null; var seen = new HashSet<string>();
            var acceptedAt = DateTimeOffset.UtcNow;
            var watchdog = WatchdogAsync(lifetime, () => peer?.LastSeen ?? acceptedAt, () => peer is not null);
            using var authDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct); authDeadline.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                await foreach (var packet in wire.ReadAsync(ct))
                {
                    if (peer is null)
                    {
                        if (authDeadline.IsCancellationRequested) break;
                        if (packet.ProtocolVersion != 2 || packet.DeviceID is not { } deviceID) { await wire.SendAsync(new Packet { Command = "pair_required", Text = "Protocol v2 pairing is required." }, ct); continue; }
                        if (packet.Command == "pair")
                        {
                            var device = await pairing.PairAsync(deviceID, packet.Hello ?? "iPhone", packet.Code, ct);
                            if (device is null) { await wire.SendAsync(new Packet { Command = "pair_error", Text = "Pairing code invalid or expired. Generate a new code on Windows." }, ct); continue; }
                            // Certificate fingerprint was explicitly checked on the phone before submitting code.
                            await wire.SendAsync(new Packet { Command = "pair", DeviceID = device.ID, Secret = device.Secret, HostID = hostID }, ct);
                            peer = new(wire, deviceID, device.Name);
                        }
                        else if (packet.Command == "authenticate" && pairing.Authenticate(hostID, deviceID, nonce, packet.Proof))
                            peer = new(wire, deviceID, pairing.Devices.First(d => d.ID == deviceID).Name);
                        else { await wire.SendAsync(new Packet { Command = "pair_required" }, ct); continue; }
                        if (peers.TryRemove(deviceID, out var old)) old.Client.Dispose();
                        peers[deviceID] = (peer, client); Changed?.Invoke();
                        await wire.SendAsync(new Packet { Command = "paired", Face = LastFace, Hello = Environment.MachineName, ProtocolVersion = 2, HostID = hostID }, ct);
                        continue;
                    }
                    peer.LastSeen = DateTimeOffset.UtcNow;
                    if (packet.Command == "ping") { await wire.SendAsync(new Packet { Command = "pong", CallID = packet.CallID }, ct); continue; }
                    if (packet.Command == "pong") continue;
                    if (packet.Command is "voice_request" or "text_request")
                    {
                        if (packet.CallID is not { Length: > 0 and < 100 } id || !seen.Add(id)) continue;
                        if (seen.Count > 1000) { await wire.SendAsync(new Packet { Command = "assistant_error", CallID = id, Text = "Reconnect to start a fresh request session." }, ct); continue; }
                    }
                    if (OnPacket is not null) await OnPacket(peer, packet, ct);
                }
            }
            finally
            {
                lifetime.Cancel(); try { await watchdog; } catch (OperationCanceledException) { }
                if (peer is not null && peers.TryGetValue(peer.DeviceID, out var active) && ReferenceEquals(active.Client, client)) { peers.TryRemove(peer.DeviceID, out _); Changed?.Invoke(); }
            }
        }
    }
    private static async Task WatchdogAsync(CancellationTokenSource lifetime, Func<DateTimeOffset> last, Func<bool> authenticated)
    {
        while (!lifetime.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
            if (DateTimeOffset.UtcNow - last() > (authenticated() ? TimeSpan.FromSeconds(60) : TimeSpan.FromMinutes(3))) lifetime.Cancel();
        }
    }
    public async Task BroadcastAsync(Packet packet, CancellationToken ct)
    {
        foreach (var entry in peers.Values)
            try { await entry.Peer.SendAsync(packet, ct); } catch (Exception e) when (e is IOException or ObjectDisposedException) { entry.Client.Dispose(); }
    }
    public void ReconnectAll() { foreach (var peer in peers.Values) peer.Client.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop(); ReconnectAll();
        if (accepting is not null) try { await accepting; } catch (OperationCanceledException) { }
        await Task.WhenAll(clients.Keys.Select(async t => { try { await t; } catch (Exception) { } }));
        stop.Dispose(); capacity.Dispose();
    }
}
