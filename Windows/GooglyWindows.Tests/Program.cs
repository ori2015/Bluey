using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GooglyWindows.Core.AI;
using GooglyWindows.Core.Audio;
using GooglyWindows.Core.Auth;
using GooglyWindows.Core.Automation;
using GooglyWindows.Core.Protocol;
using GooglyWindows.Core.Screen;
using GooglyWindows.Core.Security;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> run) => tests.Add((name, run));
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
async Task Fails<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Test("PKCE RFC7636 vector", () => Assert(OAuthPrimitives.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
Test("PKCE random entropy and syntax", () => { var a = OAuthPrimitives.RandomValue(); var b = OAuthPrimitives.RandomValue(); Assert(a != b && a.Length == 43 && !a.Contains('=')); });
Test("OAuth state rejects absent/mismatch", () => { OAuthPrimitives.ValidateState("abc", "abc"); Throws<InvalidDataException>(() => OAuthPrimitives.ValidateState("abc", null)); Throws<InvalidDataException>(() => OAuthPrimitives.ValidateState("abc", "abd")); });
Test("OAuth issued registration identity", () => { Assert(OAuthPrimitives.IssuedClient(null, "oaiapp_example") == "oaiapp_example"); Assert(OAuthPrimitives.IssuedClient("oaiapp_example", null) == "oaiapp_example"); Throws<InvalidDataException>(() => OAuthPrimitives.IssuedClient(null, "dynamic_agent_client")); Throws<InvalidDataException>(() => OAuthPrimitives.IssuedClient("a", "b")); });
AsyncTest("Refresh synchronization commits one rotating record", async () =>
{
    var saved = 0; var refreshes = 0; var writes = 0;
    var gate = new RefreshGate<int>(_ => Task.FromResult(saved), i => i == 0,
        async (_, ct) => { Interlocked.Increment(ref refreshes); await Task.Delay(10, ct); return 1; },
        (i, _) => { saved = i; writes++; return Task.CompletedTask; });
    var result = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => gate.GetAsync(default)));
    Assert(refreshes == 1 && writes == 1 && result.All(i => i == 1));
});
AsyncTest("Refresh failed persistence does not expose replacement", async () =>
{
    var gate = new RefreshGate<int>(_ => Task.FromResult(0), _ => true, (_, _) => Task.FromResult(1), (_, _) => throw new IOException());
    await Fails<IOException>(async () => await gate.GetAsync(default));
});
Test("Packet casing, optional v1 fields and Hebrew", () =>
{
    var packet = new Packet { CallID = "id", ProtocolVersion = 2, Text = "תפתח Chrome", Face = new(-1, -.5, "thinking", .2) };
    var raw = Encoding.UTF8.GetString(packet.Encode()); var decoded = Packet.Decode(packet.Encode());
    Assert(raw.Contains("\"callID\"") && !raw.Contains("accessToken") && decoded.Text == packet.Text && decoded.Face == packet.Face);
    Assert(Packet.Decode("{\"hello\":\"Mac\",\"unknownFutureField\":true}"u8).Hello == "Mac");
});
AsyncTest("Newline framing handles fragmentation and coalescence", async () =>
{
    using var raw = new FragmentedStream(Encoding.UTF8.GetBytes("{\"command\":\"ping\"}\n{\"command\":\"pong\",\"text\":\"עברית\"}\n"), 1);
    await using var wire = new PacketStream(raw); var packets = new List<Packet>(); await foreach (var packet in wire.ReadAsync(default)) packets.Add(packet);
    Assert(packets.Count == 2 && packets[1].Text == "עברית");
});
AsyncTest("Newline framing refuses truncated packet", async () =>
{
    await using var wire = new PacketStream(new MemoryStream("{\"hello\":\"x\"}"u8.ToArray()));
    await Fails<EndOfStreamException>(async () => { await foreach (var _ in wire.ReadAsync(default)) { } });
});
AsyncTest("Framing caps hostile unterminated input", async () =>
{
    await using var wire = new PacketStream(new MemoryStream(new byte[PacketStream.MaxFrameBytes + 1]));
    await Fails<InvalidDataException>(async () => { await foreach (var _ in wire.ReadAsync(default)) { } });
});
Test("Pairing HMAC and replay/session binding", () =>
{
    var secret = Pairing.NewSecret(); var proof = Pairing.Proof(secret, "host", "phone", "nonce");
    Assert(Pairing.Verify(secret, "host", "phone", "nonce", proof));
    Assert(!Pairing.Verify(secret, "host", "other", "nonce", proof) && !Pairing.Verify(secret, "host", "phone", "next", proof));
    Assert(!Pairing.Verify(secret, "host", "phone", "nonce", "invalid base64"));
});
Test("Negative virtual desktop normalized mapping", () =>
{
    var coordinates = new CoordinateMapper(new(-1920, -1080, 5760, 3240));
    var point = coordinates.FromNormalized(550, 700); var back = coordinates.ToNormalized(point);
    Assert(Math.Abs(back.X - 550) < .0001 && Math.Abs(back.Y - 700) < .0001);
    Assert(coordinates.FromNormalized(0, 0) == new PixelPoint(-1920, -1080));
    Assert(coordinates.FromNormalized(1000, 1000) == new PixelPoint(3839, 2159));
    Throws<ArgumentException>(() => coordinates.FromNormalized(double.NaN, 1));
});
foreach (var dpi in new[] { 96d, 120d, 144d, 192d }) Test($"DPI {dpi / 96:P0} overlay roundtrip", () =>
{
    var monitor = new MonitorInfo(0, new(-2560, -300, 2560, 1440), dpi, dpi, false); var original = new PixelPoint(-2011, 790);
    var restored = CoordinateMapper.FromOverlay(CoordinateMapper.ToOverlay(original, monitor), monitor);
    Assert(Math.Abs(restored.X - original.X) < .001 && Math.Abs(restored.Y - original.Y) < .001);
});
Test("Keyboard cmd-to-ctrl, F keys and validation", () =>
{
    Assert(KeyboardShortcut.Parse("cmd+shift+t").SequenceEqual(new ushort[] { 0x11, 0x10, 'T' }));
    Assert(KeyboardShortcut.Parse("F12").Single() == 0x7B && KeyboardShortcut.Parse("delete").Single() == 0x2E);
    Throws<ArgumentException>(() => KeyboardShortcut.Parse("ctrl+unknown")); Throws<ArgumentException>(() => KeyboardShortcut.Parse("a+b")); Throws<ArgumentException>(() => KeyboardShortcut.Parse("ctrl+ctrl+c"));
});
Test("Tool schema runtime validation", () =>
{
    _ = ToolCatalog.Validate("click", "{\"target\":\"C12\"}");
    Throws<ArgumentException>(() => ToolCatalog.Validate("click", "{}"));
    Throws<ArgumentException>(() => ToolCatalog.Validate("click", "{\"x\":1001,\"y\":0}"));
    Throws<ArgumentException>(() => ToolCatalog.Validate("open_url", "{\"url\":\"file:///C:/bad.exe\"}"));
    Throws<ArgumentException>(() => ToolCatalog.Validate("open_app", "{\"name\":\"C:\\\\bad.exe\"}"));
    Throws<ArgumentException>(() => ToolCatalog.Validate("type_text", "{\"text\":42}"));
    Throws<ArgumentException>(() => ToolCatalog.Validate("go_to_sleep", "{\"arbitrary\":true}"));
    Throws<ArgumentException>(() => ToolCatalog.Validate("scroll", "{\"direction\":\"sideways\"}"));
    var tools = ToolCatalog.Definitions(); Assert(tools[0]!["type"]!.ToString() == "namespace");
});
Test("Target IDs and duplicate/stale target handling", () =>
{
    var snapshot = new TargetSnapshot(new(0, 0, 100, 100), [new("C1", "Settings", "Button", new(5, 5, 20, 10)), new("L1", "שלום", "line", new(20, 20, 30, 10))], 0);
    Assert(snapshot.Find("c1").Name == "Settings" && snapshot.Describe().Contains("L1"));
    Throws<ArgumentException>(() => snapshot.Find("C99"));
});
Test("Risk classification sensitive English/Hebrew, blind clicks and commit keys", () =>
{
    Assert(ActionRiskClassifier.Classify("click", "{}", "Send", "reply") == ActionRisk.Confirmation);
    Assert(ActionRiskClassifier.Classify("click", "{}", "מחק", "clean") == ActionRisk.Confirmation);
    Assert(ActionRiskClassifier.Classify("click", "{}", "Settings", "open settings") == ActionRisk.Routine);
    Assert(ActionRiskClassifier.Classify("click", "{}", "", "click") == ActionRisk.Confirmation);
    Assert(ActionRiskClassifier.Classify("press_keys", "{\"keys\":\"enter\"}", "", "search") == ActionRisk.Confirmation);
    Assert(ActionRiskClassifier.Classify("press_keys", "{\"keys\":\"win+l\"}", "", "lock") == ActionRisk.Refused);
});
Test("Conversation uses local bounded messages and complete function pairing", () =>
{
    var history = new ConversationState(); for (var i = 0; i < 20; i++) history.Commit("u" + i, "a" + i);
    var input = history.BeginTurn("now"); Assert(input.Count == 17 && input[0]!["content"]!.ToString() == "u12");
    ConversationState.AppendToolResult(input, "call", new("OK", "jpeg")); Assert(input[^2]!["type"]!.ToString() == "function_call_output" && input[^1]!["content"]![0]!["image_url"]!.ToString().StartsWith("data:image/jpeg"));
    history.Clear(); Assert(history.BeginTurn("fresh").Count == 1);
});
AsyncTest("SSE text remains provisional until response.completed", async () =>
{
    var text = "";
    var sse = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"שלום\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n";
    var result = await ResponsesClient.ReadCompletedAsync(new FragmentedStream(Encoding.UTF8.GetBytes(sse), 3), d => { text += d; return Task.CompletedTask; }, default);
    Assert(text == "שלום" && result.Text == text);
});
AsyncTest("SSE preserves finished tool and reasoning items with an empty completion envelope", async () =>
{
    var reasoning = new JsonObject { ["type"] = "reasoning", ["id"] = "r1", ["summary"] = new JsonArray(), ["encrypted_content"] = "fixture-encrypted" };
    var call = new JsonObject { ["type"] = "function_call", ["id"] = "f1", ["call_id"] = "call-1", ["name"] = "probe", ["namespace"] = "capability", ["arguments"] = "{}", ["status"] = "completed" };
    var events = string.Join("", new[] {
        new JsonObject { ["type"] = "response.output_item.done", ["output_index"] = 1, ["item"] = call },
        new JsonObject { ["type"] = "response.output_item.done", ["output_index"] = 0, ["item"] = reasoning }
    }.Select(e => "data: " + e.ToJsonString() + "\n\n"));
    using var incomplete = new MemoryStream(Encoding.UTF8.GetBytes(events));
    await Fails<SubscriptionException>(() => ResponsesClient.ReadCompletedAsync(incomplete, _ => Task.CompletedTask, default));
    using var completed = new MemoryStream(Encoding.UTF8.GetBytes(events + "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n"));
    var result = await ResponsesClient.ReadCompletedAsync(completed, _ => Task.CompletedTask, default);
    Assert(result.Output.Count == 2 && result.Output[0]?["encrypted_content"]?.ToString() == "fixture-encrypted");
    Assert(result.Output[1]?["call_id"]?.ToString() == "call-1" && result.Output[1]?["namespace"]?.ToString() == "capability");
});
AsyncTest("SSE failed, incomplete and disconnect never count as success", async () =>
{
    foreach (var ending in new[] { "", "data: {\"type\":\"response.incomplete\"}\n\n", "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n" })
        await Fails<SubscriptionException>(async () => await ResponsesClient.ReadCompletedAsync(new MemoryStream(Encoding.UTF8.GetBytes("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n" + ending)), _ => Task.CompletedTask, default));
});
AsyncTest("HTTP subscription contract and usage-limit circuit breaker", async () =>
{
    var count = 0;
    var handler = new FakeHttp(async request =>
    {
        count++; Assert(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses"); Assert(request.Headers.Authorization?.Scheme == "Bearer");
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
        Assert(body["store"]!.GetValue<bool>() == false && body["stream"]!.GetValue<bool>() && body["input"] is JsonArray && body["previous_response_id"] is null);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n") };
    });
    var client = new ResponsesClient(new HttpClient(handler), new FakeToken());
    for (var i = 0; i < 2; i++) await Fails<SubscriptionException>(async () => await client.CompleteAsync("discovered-model", new JsonArray(ConversationState.Message("user", "hello")), "", [], _ => Task.CompletedTask, default));
    Assert(count == 1 && client.UsagePaused);
});
AsyncTest("Function-call loop uses outputs, vision context and final completion", async () =>
{
    var responses = new FakeResponses(); var tools = new FakeTools(); var state = new ConversationState(); var loop = new AgentLoop(responses, tools, state);
    var final = await loop.RunAsync("discovered", "what's on screen", ToolCatalog.Definitions(), _ => Task.CompletedTask, (_, _, _) => Task.CompletedTask, default);
    Assert(final == "Settings." && tools.Calls == 1 && responses.Calls == 2 && state.BeginTurn("next").Count == 3);
});
AsyncTest("TCP reconnect and iPhone fixtures", async () =>
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
    try
    {
        for (var i = 0; i < 2; i++)
        {
            using var client = new System.Net.Sockets.TcpClient(); var connect = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var server = await listener.AcceptTcpClientAsync(); await connect;
            await using var sending = new PacketStream(client.GetStream()); await using var receiving = new PacketStream(server.GetStream());
            await sending.SendAsync(new Packet { Command = "ping", CallID = "reconnected" + i }, default);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await foreach (var p in receiving.ReadAsync(timeout.Token)) { Assert(p.CallID == "reconnected" + i); break; }
        }
    }
    finally { listener.Stop(); }
    foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "iphone-v2.jsonl"))) { var packet = Packet.Decode(Encoding.UTF8.GetBytes(line)); Assert(Packet.Decode(packet.Encode()) == packet); }
});
Test("WAV conversion validates and resamples 24k to 16k", () =>
{
    var wave = new byte[44 + 4800]; "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8); "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
    BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), 1); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), 24000); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16); "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), 4800);
    var result = WhisperTranscriber.To16kWav(wave); Assert(result.Length == 44 + 3200 && BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(24)) == 16000);
    wave[0] = 0; Throws<ArgumentException>(() => WhisperTranscriber.To16kWav(wave));
});

AsyncTest("Production OAuth code flow, OIDC/JWKS and restart refresh", async () =>
{
    using var key = RSA.Create(2048);
    var parameters = key.ExportParameters(false);
    var jwk = new JsonObject { ["kty"] = "RSA", ["kid"] = "test", ["use"] = "sig", ["alg"] = "RS256", ["n"] = OAuthPrimitives.Base64Url(parameters.Modulus!), ["e"] = OAuthPrimitives.Base64Url(parameters.Exponent!) };
    var store = new GooglyWindows.Security.ProtectedStore();
    string? nonce = null; string? redirect = null; string? verifierChallenge = null; var refreshes = 0;
    var handler = new FakeHttp(async request =>
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url.EndsWith("openid-configuration")) return JsonResponse(new JsonObject { ["issuer"] = "https://auth.openai.com", ["jwks_uri"] = "https://auth.openai.com/.well-known/jwks.json", ["revocation_endpoint"] = "https://auth.openai.com/api/accounts/oauth/revoke" });
        if (url.EndsWith("jwks.json")) return JsonResponse(new JsonObject { ["keys"] = new JsonArray(jwk.DeepClone()) });
        var form = ParseQuery(await request.Content!.ReadAsStringAsync());
        Assert(form["client_id"] == "oaiapp_test" && form["resource"] == "https://api.openai.com/v1" && !form.ContainsKey("client_secret"));
        if (form["grant_type"] == "refresh_token")
        {
            Interlocked.Increment(ref refreshes); Assert(form["refresh_token"] == "refresh0"); await Task.Delay(10);
            return JsonResponse(new JsonObject { ["access_token"] = "access1", ["refresh_token"] = "refresh1", ["expires_in"] = 3600, ["token_type"] = "Bearer" });
        }
        Assert(form["redirect_uri"] == redirect && OAuthPrimitives.Challenge(form["code_verifier"]) == verifierChallenge);
        Assert((await store.ReadAsync<GooglyWindows.Auth.AccountRecord>("chatgpt-account", default))?.ClientID == "oaiapp_test");
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("https://auth.openai.com", "oaiapp_test", [new("sub", "test-sub"), new("email", "test@example.com"), new("nonce", nonce!)], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.RsaSecurityKey(key) { KeyId = "test" }, "RS256"));
        return JsonResponse(new JsonObject { ["id_token"] = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt), ["access_token"] = "access0", ["refresh_token"] = "refresh0", ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", ["expires_in"] = 1, ["token_type"] = "Bearer" });
    });
    Task browser = Task.CompletedTask;
    var auth = new GooglyWindows.Auth.ChatGptAuth(new HttpClient(handler), store, url =>
    {
        var uri = new Uri(url); var query = ParseQuery(uri.Query.TrimStart('?'));
        Assert(uri.Host == "auth.openai.com" && query["client_id"] == "dynamic_agent_client" && query["agent_name_hint"] == "Googly Eyes");
        nonce = query["nonce"]; redirect = query["redirect_uri"]; verifierChallenge = query["code_challenge"];
        Assert(new Uri(redirect).Host == "127.0.0.1" && query["code_challenge_method"] == "S256");
        browser = SendCallbackAsync(redirect, query["state"], "oaiapp_test");
    });
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    await auth.SignInAsync(timeout.Token); await browser;
    Assert(auth.Account?.Sharing == true && auth.Account.Subject == "test-sub");
    var restarted = new GooglyWindows.Auth.ChatGptAuth(new HttpClient(handler), store); await restarted.InitializeAsync(timeout.Token);
    var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => restarted.GetAccessTokenAsync(timeout.Token)));
    Assert(refreshes == 1 && tokens.All(t => t == "access1") && restarted.Account?.RefreshToken == "refresh1");
});
AsyncTest("Production OAuth rejects invalid nonce and signature", async () =>
{
    foreach (var tamper in new[] { "nonce", "signature", "issuer", "audience", "expiration" })
    {
        using var key = RSA.Create(2048); using var other = RSA.Create(2048); var publicKey = key.ExportParameters(false);
        string nonce = "";
        var handler = new FakeHttp(async request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("openid-configuration")) return JsonResponse(new JsonObject { ["issuer"] = "https://auth.openai.com", ["jwks_uri"] = "https://auth.openai.com/.well-known/jwks.json" });
            if (url.EndsWith("jwks.json")) return JsonResponse(new JsonObject { ["keys"] = new JsonArray(new JsonObject { ["kty"] = "RSA", ["kid"] = "test", ["n"] = OAuthPrimitives.Base64Url(publicKey.Modulus!), ["e"] = OAuthPrimitives.Base64Url(publicKey.Exponent!) }) });
            _ = await request.Content!.ReadAsStringAsync();
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(tamper == "issuer" ? "https://other.example" : "https://auth.openai.com", tamper == "audience" ? "wrong-client" : "oaiapp_test", [new("sub", "test"), new("nonce", tamper == "nonce" ? "wrong" : nonce)], DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(tamper == "expiration" ? -5 : 5), new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.RsaSecurityKey(tamper == "signature" ? other : key) { KeyId = "test" }, "RS256"));
            return JsonResponse(new JsonObject { ["id_token"] = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt), ["access_token"] = "test-access", ["refresh_token"] = "test-refresh", ["scope"] = "chatgpt.tokens.use.direct", ["expires_in"] = 3600, ["token_type"] = "Bearer" });
        });
        var store = new GooglyWindows.Security.ProtectedStore(); Task callback = Task.CompletedTask;
        var auth = new GooglyWindows.Auth.ChatGptAuth(new HttpClient(handler), store, url => { var query = ParseQuery(new Uri(url).Query.TrimStart('?')); nonce = query["nonce"]; callback = SendCallbackAsync(query["redirect_uri"], query["state"], "oaiapp_test"); });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var rejected = false;
        try { await auth.SignInAsync(timeout.Token); } catch (Exception e) when (e is Microsoft.IdentityModel.Tokens.SecurityTokenException or InvalidDataException) { rejected = true; }
        await callback; Assert(rejected && auth.Account?.Sharing != true, "OIDC tamper accepted: " + tamper);
    }
});
AsyncTest("Production TLS server rejects unpaired voice and reconnects with HMAC", async () =>
{
    var store = new GooglyWindows.Security.ProtectedStore(); var registry = new GooglyWindows.Networking.PairingRegistry(store); await registry.LoadAsync(default);
    using var certificate = await GooglyWindows.Networking.HostCertificate.LoadAsync(store, default);
    await using var server = new GooglyWindows.Networking.PhoneServer(registry, certificate, "urn:uuid:test"); var handled = 0;
    server.OnPacket = (_, packet, _) => { if (packet.Command == "voice_request") handled++; return Task.CompletedTask; }; server.Start();
    var device = Guid.NewGuid().ToString(); string? secret = null;
    for (var attempt = 0; attempt < 2; attempt++)
    {
        using var tcp = new System.Net.Sockets.TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, server.Port);
        using var tls = new System.Net.Security.SslStream(tcp.GetStream(), false, (_, remote, _, _) => remote is not null && SHA256.HashData(remote.GetRawCertData()).SequenceEqual(SHA256.HashData(certificate.RawData)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await tls.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions { TargetHost = "googly-test" }, timeout.Token);
        await using var wire = new PacketStream(tls); await using var iterator = wire.ReadAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert(await iterator.MoveNextAsync()); var challenge = iterator.Current;
        if (attempt == 0)
        {
            await wire.SendAsync(new Packet { Command = "voice_request", CallID = "unauthorized", Audio = "none" }, timeout.Token);
            Assert(await iterator.MoveNextAsync() && iterator.Current.Command == "pair_required" && handled == 0);
            var code = registry.BeginPairing();
            await wire.SendAsync(new Packet { Command = "pair", ProtocolVersion = 2, DeviceID = device, Hello = "test iPhone", Code = code }, timeout.Token);
            Assert(await iterator.MoveNextAsync() && iterator.Current.Command == "pair"); secret = iterator.Current.Secret;
        }
        else await wire.SendAsync(new Packet { Command = "authenticate", ProtocolVersion = 2, DeviceID = device, Proof = Pairing.Proof(secret!, challenge.HostID!, device, challenge.Nonce!) }, timeout.Token);
        Assert(await iterator.MoveNextAsync() && iterator.Current.Command == "paired");
        await wire.SendAsync(new Packet { Command = "ping", CallID = "keepalive" }, timeout.Token);
        Assert(await iterator.MoveNextAsync() && iterator.Current.Command == "pong");
        await wire.SendAsync(new Packet { Command = "voice_request", CallID = "valid-" + attempt, Audio = "fixture" }, timeout.Token);
        await wire.SendAsync(new Packet { Command = "ping", CallID = "after" }, timeout.Token); Assert(await iterator.MoveNextAsync());
    }
    Assert(handled == 2 && registry.Devices.Count == 1);
});
AsyncTest("Native Windows mDNS binds IPv4 discovery interfaces", async () =>
{
    if (!OperatingSystem.IsWindows()) return;
    Assert(System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback), "Windows has no active network interface");
    await using var advertiser = new GooglyWindows.Networking.MdnsAdvertiser(12345, "urn:uuid:edfe4340-9f2b-4779-aa5c-30908699dc16");
    advertiser.Start();
    Assert(advertiser.AdvertisedInterfaceCount > 0, "No IPv4 interface bound for Bonjour");
});
AsyncTest("Production pairing code rate limit and protected persistence contract", async () =>
{
    var store = new GooglyWindows.Security.ProtectedStore(); var registry = new GooglyWindows.Networking.PairingRegistry(store);
    var code = registry.BeginPairing(); var device = Guid.NewGuid().ToString();
    for (var i = 0; i < 5; i++) Assert(await registry.PairAsync(device, "phone", "bad", default) is null);
    Assert(await registry.PairAsync(device, "phone", code, default) is null);
    code = registry.BeginPairing(); var record = await registry.PairAsync(device, "phone", code, default); Assert(record is not null);
    var restarted = new GooglyWindows.Networking.PairingRegistry(store); await restarted.LoadAsync(default); Assert(restarted.Devices.Single().Secret == record!.Secret);
});
Test("Production log redaction removes bearer, keys, JWT and token fields", () =>
{
    var text = GooglyWindows.Logging.SafeLog.Redact("Bearer test-secret access_token=raw refresh_token=raw id_token=raw sk-test-key eyJabc.def.ghi");
    Assert(!text.Contains("test-secret") && !text.Contains("=raw") && !text.Contains("sk-test") && !text.Contains("eyJabc"));
});

static HttpResponseMessage JsonResponse(JsonNode json) => new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
static Dictionary<string, string> ParseQuery(string query) => query.Split('&').Select(s => s.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString((p.Length > 1 ? p[1] : "").Replace('+', ' ')));
static async Task SendCallbackAsync(string redirect, string state, string client)
{
    using var http = new HttpClient(); await http.GetStringAsync(redirect + "?code=test-code&state=" + Uri.EscapeDataString(state) + "&client_id=" + client);
}

if (Environment.GetEnvironmentVariable("BLUEY_WHISPER_EXE") is { } whisperExe && Environment.GetEnvironmentVariable("BLUEY_WHISPER_MODEL") is { } whisperModel && Environment.GetEnvironmentVariable("BLUEY_WHISPER_WAV") is { } whisperWav)
{
    AsyncTest("Real local Whisper CLI transcribes English audio without API", async () =>
    {
        var transcriber = new WhisperTranscriber { Executable = whisperExe, Model = whisperModel };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var timer = System.Diagnostics.Stopwatch.StartNew();
        var text = await transcriber.TranscribeAsync(await File.ReadAllBytesAsync(whisperWav, timeout.Token), timeout.Token);
        Assert(text.Contains("country", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"Local Whisper fixture elapsed: {timer.Elapsed.TotalMilliseconds:F0} ms");
    });
}

AsyncTest("Refresh rotation persists before honoring cancellation", async () =>
{
    using var cancelled = new CancellationTokenSource(); var persisted = false;
    var gate = new RefreshGate<int>(_ => Task.FromResult(0), _ => true,
        (_, _) => { cancelled.Cancel(); return Task.FromResult(1); },
        (value, token) => { Assert(!token.IsCancellationRequested && value == 1); persisted = true; return Task.CompletedTask; });
    await Fails<OperationCanceledException>(async () => await gate.GetAsync(cancelled.Token));
    Assert(persisted);
});

var failed = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed"); return failed == 0 ? 0 : 1;

sealed class FragmentedStream(byte[] data, int chunk) : MemoryStream(data)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], ct);
}
sealed class FakeToken : IAccessTokenProvider { public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("test-only-token"); }
sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
}
sealed class FakeTools : IToolExecutor
{
    public int Calls { get; private set; }
    public Task<ToolResult> ExecuteAsync(string name, string arguments, string intent, CancellationToken ct) { Calls++; if (name != "look_at_screen") throw new Exception("Wrong tool"); return Task.FromResult(new ToolResult("C1 Settings", "jpeg-fixture")); }
}
sealed class FakeResponses : IResponsesClient
{
    public int Calls { get; private set; }
    public async Task<CompletedResponse> CompleteAsync(string model, JsonArray input, string instructions, JsonArray tools, Func<string, Task> delta, CancellationToken ct)
    {
        Calls++;
        if (Calls == 1) return new(new JsonArray(new JsonObject { ["type"] = "function_call", ["namespace"] = "computer", ["name"] = "look_at_screen", ["call_id"] = "c1", ["arguments"] = "{}" }), "");
        if (!input.Any(n => n?["type"]?.ToString() == "function_call_output") || !input.Any(n => n?["content"] is JsonArray)) throw new Exception("Missing tool/image context");
        await delta("Settings."); return new(new JsonArray(), "Settings.");
    }
}
