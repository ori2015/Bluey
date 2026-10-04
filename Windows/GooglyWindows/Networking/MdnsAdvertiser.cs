using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using GooglyWindows.Logging;
namespace GooglyWindows.Networking;
// RFC 6762/6763: PTR, SRV, TXT and A responses. Bonjour discovers the original service type.
public sealed class MdnsAdvertiser(int port, string hostID) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<UdpClient> sockets = [];
    private readonly List<Task> workers = [];
    public int AdvertisedInterfaceCount => sockets.Count;
    private const string Service = "_googly._tcp.local";
    private readonly string instance = "Googly-" + Environment.MachineName.Replace('.', '-') + "-" + hostID[^8..] + "." + Service;
    private readonly string host = "googly-" + hostID[^12..].Replace('-', '0') + ".local";
    public void Start()
    {
        foreach (var address in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                     .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork))
        {
            try
            {
                var udp = new UdpClient(AddressFamily.InterNetwork); udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353)); udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"), address);
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                sockets.Add(udp); workers.Add(RunAsync(udp, address, stop.Token));
            }
            catch (SocketException) { SafeLog.Event("Warning", "mdns", "interface_unavailable"); }
        }
        if (sockets.Count == 0) SafeLog.Event("Warning", "mdns", "no_ipv4_interface");
    }
    private async Task RunAsync(UdpClient udp, IPAddress address, CancellationToken ct)
    {
        var announce = AnnounceAsync(udp, address, ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var query = await udp.ReceiveAsync(ct);
                var bytes = query.Buffer;
                if (bytes.Length < 12 || (bytes[2] & 0x80) != 0) continue;
                // Respond only to bounded valid questions about this service/host.
                if (!Matches(bytes)) continue;
                var destination = query.RemoteEndPoint.Port == 5353 ? new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353) : query.RemoteEndPoint;
                await udp.SendAsync(Answer(address, 120, query.RemoteEndPoint.Port == 5353 ? (ushort)0 : BinaryPrimitives.ReadUInt16BigEndian(bytes)), destination, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
        finally { try { await announce; } catch (OperationCanceledException) { } }
    }
    private async Task AnnounceAsync(UdpClient udp, IPAddress address, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await udp.SendAsync(Answer(address, 120, 0), new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), ct);
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
        }
    }
    private bool Matches(byte[] packet)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4)); if (count > 100) return false;
        var pos = 12;
        for (var i = 0; i < count; i++)
        {
            var name = ReadName(packet, ref pos); if (name is null || pos + 4 > packet.Length) return false;
            pos += 4;
            if (name.Equals(Service, StringComparison.OrdinalIgnoreCase) || name.Equals(instance, StringComparison.OrdinalIgnoreCase) || name.Equals(host, StringComparison.OrdinalIgnoreCase) || name == "_services._dns-sd._udp.local") return true;
        }
        return false;
    }
    private static string? ReadName(byte[] data, ref int pos)
    {
        var labels = new List<string>(); var index = pos; var jumped = false;
        for (var depth = 0; depth < 64 && index < data.Length; depth++)
        {
            var n = data[index++];
            if (n == 0) { if (!jumped) pos = index; return string.Join('.', labels); }
            if ((n & 0xC0) == 0xC0)
            {
                if (index >= data.Length) return null;
                var offset = ((n & 63) << 8) | data[index++]; if (!jumped) pos = index; jumped = true; index = offset; continue;
            }
            if (n > 63 || index + n > data.Length) return null;
            labels.Add(Encoding.UTF8.GetString(data, index, n)); index += n;
        }
        return null;
    }
    private byte[] Answer(IPAddress address, uint ttl, ushort id)
    {
        using var stream = new MemoryStream();
        void U16(ushort n) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, n); stream.Write(b); }
        void U32(uint n) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); stream.Write(b); }
        void Record(string name, ushort type, byte[] value) { stream.Write(Name(name)); U16(type); U16(type == 12 ? (ushort)1 : (ushort)0x8001); U32(ttl); U16((ushort)value.Length); stream.Write(value); }
        U16(id); U16(0x8400); U16(0); U16(5); U16(0); U16(0);
        Record("_services._dns-sd._udp.local", 12, Name(Service)); Record(Service, 12, Name(instance));
        var srv = new byte[6].Concat(Name(host)).ToArray(); BinaryPrimitives.WriteUInt16BigEndian(srv.AsSpan(4), (ushort)port); Record(instance, 33, srv);
        var txt = Encoding.UTF8.GetBytes("protocolVersion=2"); Record(instance, 16, new byte[] { (byte)txt.Length }.Concat(txt).ToArray());
        Record(host, 1, address.GetAddressBytes()); return stream.ToArray();
    }
    private static byte[] Name(string name)
    {
        using var s = new MemoryStream(); foreach (var label in name.Split('.')) { var b = Encoding.UTF8.GetBytes(label); if (b.Length > 63) throw new InvalidDataException("mDNS label too long."); s.WriteByte((byte)b.Length); s.Write(b); } s.WriteByte(0); return s.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); foreach (var socket in sockets) socket.Dispose();
        try { await Task.WhenAll(workers); } catch (OperationCanceledException) { } stop.Dispose();
    }
}
