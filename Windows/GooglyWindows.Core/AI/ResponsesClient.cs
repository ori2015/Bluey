using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
namespace GooglyWindows.Core.AI;

public sealed record CompletedResponse(JsonArray Output, string Text);
public interface IResponsesClient
{
    Task<CompletedResponse> CompleteAsync(string model, JsonArray input, string instructions, JsonArray tools, Func<string, Task> delta, CancellationToken ct);
}
public sealed class ResponsesClient(HttpClient http, IAccessTokenProvider auth) : IResponsesClient
{
    public bool UsagePaused { get; private set; }
    public void ResumeUsage() => UsagePaused = false;
    public async Task<JsonArray> ListModelsAsync(CancellationToken ct)
    {
        using var request = await RequestAsync(HttpMethod.Get, "https://api.openai.com/v1/models", ct);
        using var response = await http.SendAsync(request, ct);
        var body = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!response.IsSuccessStatusCode) throw SubscriptionException.Parse(body, response.StatusCode);
        return body?["models"]?.AsArray() ?? throw new InvalidDataException("Missing account model catalog.");
    }
    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(ct));
        return request;
    }
    public async Task<CompletedResponse> CompleteAsync(string model, JsonArray input, string instructions, JsonArray tools, Func<string, Task> delta, CancellationToken ct)
    {
        if (UsagePaused) throw new SubscriptionException("subscription_sharing_usage_limit_exceeded");
        // No private endpoints, API keys, previous_response_id or preview-excluded fields.
        var body = new JsonObject { ["model"] = model, ["input"] = input.DeepClone(), ["instructions"] = instructions, ["store"] = false, ["stream"] = true, ["include"] = new JsonArray("reasoning.encrypted_content") };
        if (tools.Count > 0) body["tools"] = tools.DeepClone();
        for (var attempt = 0; ; attempt++)
        {
            using var request = await RequestAsync(HttpMethod.Post, "https://api.openai.com/v1/responses", ct);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException) when (attempt < 2) { await Task.Delay(TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt)), ct); continue; }
            using (response)
            {
                var requestID = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
                if (!response.IsSuccessStatusCode)
                {
                    JsonNode? error = null;
                    try { error = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)); } catch (System.Text.Json.JsonException) { }
                    var failure = SubscriptionException.Parse(error, response.StatusCode, requestID);
                    if (failure.Code == "subscription_sharing_usage_limit_exceeded") UsagePaused = true;
                    if (failure.IsTransient && attempt < 2) { await Task.Delay(TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt)), ct); continue; }
                    throw failure;
                }
                // Never replay a partially streamed inference automatically.
                try { return await ReadCompletedAsync(await response.Content.ReadAsStreamAsync(ct), delta, ct); }
                catch (SubscriptionException ex) { if (ex.Code == "subscription_sharing_usage_limit_exceeded") UsagePaused = true; throw; }
                catch (IOException) { throw new SubscriptionException("stream_interrupted", requestID: requestID); }
            }
        }
    }
    public static async Task<CompletedResponse> ReadCompletedAsync(Stream stream, Func<string, Task> delta, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var data = new StringBuilder();
        var text = new StringBuilder();
        var finishedItems = new SortedDictionary<int, JsonNode>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length > 8 * 1024 * 1024 || data.Length > 16 * 1024 * 1024) throw new InvalidDataException("SSE event too large.");
            if (line.StartsWith("data:", StringComparison.Ordinal)) { if (data.Length > 0) data.Append('\n'); data.Append(line.AsSpan(5).TrimStart()); }
            else if (line.Length == 0 && data.Length > 0)
            {
                var raw = data.ToString(); data.Clear();
                if (raw == "[DONE]") break;
                var e = JsonNode.Parse(raw) ?? throw new InvalidDataException("Empty SSE event.");
                switch (e["type"]?.GetValue<string>())
                {
                    case "response.output_item.done":
                        var index = e["output_index"]?.GetValue<int>() ?? throw new InvalidDataException("Missing output item index.");
                        if (index is < 0 or >= 256) throw new InvalidDataException("Too many response output items.");
                        finishedItems[index] = e["item"]?.DeepClone() ?? throw new InvalidDataException("Missing finished output item.");
                        break;
                    case "response.output_text.delta": var part = e["delta"]?.GetValue<string>() ?? ""; text.Append(part); await delta(part); break;
                    case "error": case "response.failed": throw SubscriptionException.Parse(e, null);
                    case "response.incomplete": throw new SubscriptionException("response_incomplete");
                    case "response.completed":
                        var response = e["response"] ?? throw new InvalidDataException("Missing completed response.");
                        if (response["status"]?.GetValue<string>() != "completed") throw new SubscriptionException("response_incomplete");
                        // The live plan-sharing route sends finished items in
                        // output_item.done, with an empty output in completed.
                        // Keep those items provisional until response.completed.
                        var output = response["output"]?.AsArray();
                        if (output is null && finishedItems.Count == 0) throw new InvalidDataException("Missing response output.");
                        if (output is null || output.Count == 0)
                            output = new JsonArray(finishedItems.Values.Select(i => i.DeepClone()).ToArray());
                        var finalText = string.Concat(output.Where(i => i?["type"]?.ToString() == "message").SelectMany(i => i?["content"]?.AsArray() ?? []).Where(c => c?["type"]?.ToString() == "output_text").Select(c => c?["text"]?.ToString()));
                        return new((JsonArray)output.DeepClone(), finalText.Length > 0 ? finalText : text.ToString());
                }
            }
        }
        throw new SubscriptionException("stream_interrupted");
    }
}
