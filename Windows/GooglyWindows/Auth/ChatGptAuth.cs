using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using GooglyWindows.Core.Auth;
using GooglyWindows.Core.AI;
using GooglyWindows.Security;
namespace GooglyWindows.Auth;

public sealed record AccountRecord(string ClientID, string HostID, string Subject = "", string Email = "", string? AccessToken = null,
    string? RefreshToken = null, string? IDToken = null, string[]? Scopes = null, DateTimeOffset Expires = default)
{
    public bool Sharing => AccessToken is not null && Scopes?.Contains("chatgpt.tokens.use.direct") == true;
}
public sealed class ChatGptAuth(HttpClient http, ProtectedStore store, Action<string>? openBrowser = null) : IAccessTokenProvider
{
    private const string Issuer = "https://auth.openai.com";
    private const string Resource = "https://api.openai.com/v1";
    private const string TokenURL = Issuer + "/api/accounts/oauth/token";
    private readonly SemaphoreSlim gate = new(1); // App enforces one process per Windows user.
    private readonly ConfigurationManager<OpenIdConnectConfiguration> oidc = new(Issuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(http) { RequireHttps = true });
    public AccountRecord? Account { get; private set; }
    public event Action? Changed;
    public async Task InitializeAsync(CancellationToken ct)
    {
        Account = await store.ReadAsync<AccountRecord>("chatgpt-account", ct);
        Changed?.Invoke();
    }
    public async Task<string> HostIDAsync(CancellationToken ct)
    {
        var id = await store.ReadAsync<string>("host-id", ct);
        if (id is not null) return id;
        id = "urn:uuid:" + Guid.NewGuid(); await store.SaveAsync("host-id", id, ct); return id;
    }
    public async Task SignInAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5)); ct = timeout.Token;
            var existing = Account;
            var host = await HostIDAsync(ct);
            var verifier = OAuthPrimitives.RandomValue(); var state = OAuthPrimitives.RandomValue(); var nonce = OAuthPrimitives.RandomValue();
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(4);
            try
            {
                var callback = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/auth/callback";
                var parameters = new Dictionary<string, string>
                {
                    ["client_id"] = existing?.ClientID ?? "dynamic_agent_client", ["ext_agent_host_id"] = host,
                    ["response_type"] = "code", ["redirect_uri"] = callback,
                    ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", ["resource"] = Resource,
                    ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256", ["code_challenge"] = OAuthPrimitives.Challenge(verifier)
                };
                if (existing is null) parameters["agent_name_hint"] = "Googly Eyes";
                else
                {
                    if (existing.IDToken is not null) parameters["id_token_hint"] = existing.IDToken;
                    if (existing.Email.Length > 0) parameters["login_hint"] = existing.Email;
                    if (!existing.Sharing) parameters["prompt"] = "consent";
                }
                var authorizationURL = Issuer + "/api/accounts/authorize?" + Query(parameters);
                if (openBrowser is not null) openBrowser(authorizationURL);
                else Process.Start(new ProcessStartInfo(authorizationURL) { UseShellExecute = true });
                var query = await ListenAsync(listener, state, ct);
                if (query.ContainsKey("error")) throw new InvalidOperationException("ChatGPT sign-in was declined.");
                var issued = OAuthPrimitives.IssuedClient(existing?.ClientID, query.GetValueOrDefault("client_id"));
                var registration = existing ?? new AccountRecord(issued, host);
                // Retain registration if an authorization code expires, without replacing an active token set.
                await store.SaveAsync("chatgpt-account", registration, ct); Account = registration;
                var code = query.GetValueOrDefault("code") ?? throw new InvalidDataException("Missing authorization code.");
                var token = await TokenAsync(new() { ["grant_type"] = "authorization_code", ["client_id"] = issued, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = callback, ["resource"] = Resource }, ct);
                var idToken = Required(token, "id_token");
                var configuration = await oidc.GetConfigurationAsync(ct);
                var validator = new JwtSecurityTokenHandler { MapInboundClaims = false };
                var principal = validator.ValidateToken(idToken, new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = issued,
                    ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true, IssuerSigningKeys = configuration.SigningKeys, ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256]
                }, out _);
                OAuthPrimitives.ValidateState(nonce, principal.FindFirst("nonce")?.Value);
                var subject = principal.FindFirst("sub")?.Value ?? throw new InvalidDataException("Missing verified subject.");
                if (existing?.Subject is { Length: > 0 } old && subject != old) throw new InvalidDataException("Reconnect must use the saved ChatGPT account. Sign out to change accounts.");
                var record = FromToken(registration with { Subject = subject, Email = principal.FindFirst("email")?.Value ?? "ChatGPT account" }, token);
                if (!string.Equals(token.GetProperty("token_type").GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported token type.");
                await store.SaveAsync("chatgpt-account", record, ct); Account = record; Changed?.Invoke();
            }
            finally { listener.Stop(); }
        }
        finally { gate.Release(); }
    }
    private RefreshGate<AccountRecord>? tokens;
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        tokens ??= new RefreshGate<AccountRecord>(_ =>
        {
            var record = Account ?? throw new SubscriptionException("sign_in_required");
            if (!record.Sharing) throw new SubscriptionException("chatpass_v2_scope_not_authorized");
            return Task.FromResult(record);
        }, record => record.Expires <= DateTimeOffset.UtcNow.AddMinutes(2), RefreshAsync,
        async (next, token) => { await store.SaveAsync("chatgpt-account", next, token); Account = next; Changed?.Invoke(); }, gate);
        var result = await tokens.GetAsync(ct);
        if (!result.Sharing) throw new SubscriptionException("chatpass_v2_scope_not_authorized");
        return result.AccessToken!;
    }
    private async Task<AccountRecord> RefreshAsync(AccountRecord record, CancellationToken ct)
    {
        if (record.RefreshToken is null) throw new SubscriptionException("sign_in_required");
        try
        {
            var token = await TokenAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = record.ClientID, ["refresh_token"] = record.RefreshToken, ["resource"] = Resource }, ct);
            return FromToken(record, token);
        }
        catch (SubscriptionException ex) when (ex.Code is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused" or "refresh_token_invalidated")
        {
            Account = record with { AccessToken = null, RefreshToken = null, IDToken = null };
            await store.SaveAsync("chatgpt-account", Account, ct); Changed?.Invoke(); throw;
        }
    }
    public async Task<bool> SignOutAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var record = Account; if (record is null) return true;
            var revoked = record.RefreshToken is null;
            if (record.RefreshToken is not null)
            {
                try
                {
                    var configuration = await oidc.GetConfigurationAsync(ct);
                    var endpoint = configuration.AdditionalData.TryGetValue("revocation_endpoint", out var value) ? value?.ToString() : null;
                    if (endpoint is not null && new Uri(endpoint).Host == "auth.openai.com" && new Uri(endpoint).Scheme == "https")
                    {
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = record.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = record.ClientID }), ct);
                            if (response.StatusCode == HttpStatusCode.OK) { revoked = true; break; }
                            if ((int)response.StatusCode < 500) break;
                            await Task.Delay(300 * (1 << attempt), ct);
                        }
                    }
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
            }
            Account = record with { AccessToken = null, RefreshToken = null, IDToken = null, Scopes = [] };
            await store.SaveAsync("chatgpt-account", Account, CancellationToken.None); Changed?.Invoke(); return revoked;
        }
        finally { gate.Release(); }
    }
    private async Task<JsonElement> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        // A token rotation must not be blindly retried after uncertain transport completion.
        using var response = await http.PostAsync(TokenURL, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = "sign_in_required";
            try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var error)) code = error.ValueKind == JsonValueKind.String ? error.GetString()! : error.TryGetProperty("code", out var c) ? c.GetString()! : code; } catch (JsonException) { }
            throw new SubscriptionException(code, response.StatusCode);
        }
        using var parsed = JsonDocument.Parse(body); return parsed.RootElement.Clone();
    }
    private static AccountRecord FromToken(AccountRecord prior, JsonElement token) => prior with
    {
        AccessToken = Required(token, "access_token"), RefreshToken = token.TryGetProperty("refresh_token", out var r) ? r.GetString() : prior.RefreshToken,
        IDToken = token.TryGetProperty("id_token", out var i) ? i.GetString() : prior.IDToken,
        Scopes = token.TryGetProperty("scope", out var s) ? s.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries) : prior.Scopes,
        Expires = DateTimeOffset.UtcNow.AddSeconds(token.GetProperty("expires_in").GetInt32())
    };
    private static string Required(JsonElement e, string property) => e.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing credential field.");
    private static string Query(Dictionary<string, string> values) => string.Join('&', values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    private static async Task<Dictionary<string, string>> ListenAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(ct); perRequest.CancelAfter(TimeSpan.FromSeconds(5));
            var stream = client.GetStream(); var buffer = new List<byte>();
            try
            {
                while (buffer.Count < 16384)
                {
                    var one = new byte[1]; if (await stream.ReadAsync(one, perRequest.Token) == 0) break; buffer.Add(one[0]);
                    if (buffer.Count >= 4 && buffer.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                }
                var line = Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n")[0].Split(' ');
                if (line.Length != 3 || line[0] != "GET" || !Uri.TryCreate("http://127.0.0.1" + line[1], UriKind.Absolute, out var uri) || uri.AbsolutePath != "/auth/callback") continue;
                var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToArray();
                if (pairs.GroupBy(p => p[0]).Any(g => g.Count() > 1)) continue;
                var query = pairs.ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString((p.Length > 1 ? p[1] : "").Replace('+', ' ')));
                OAuthPrimitives.ValidateState(state, query.GetValueOrDefault("state"));
                var html = "You can return to Googly Eyes. The app will verify your sign-in.";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {html.Length}\r\nConnection: close\r\n\r\n{html}"), ct);
                return query;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or OperationCanceledException && !ct.IsCancellationRequested) { }
        }
    }
}
