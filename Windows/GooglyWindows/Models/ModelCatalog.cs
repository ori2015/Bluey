using System.Text.Json.Nodes;
using GooglyWindows.Core.AI;
namespace GooglyWindows.Models;
public sealed record ModelCapabilities(bool Text, bool Vision, bool Tools, bool ReasoningObserved, bool WebSearch = false);
public sealed record ModelChoice(string Slug, string DisplayName)
{
    public ModelCapabilities? Capabilities { get; set; }
    public override string ToString() => DisplayName;
}
public sealed class ModelCatalog(ResponsesClient responses)
{
    public List<ModelChoice> Models { get; private set; } = [];
    public ModelChoice? Active { get; private set; }
    public string WebStatus => Active?.Capabilities?.WebSearch == true ? "Available through ChatGPT Plan" : "Web research isn't available through the connected ChatGPT plan.";
    public async Task DiscoverAsync(CancellationToken ct)
    {
        var list = await responses.ListModelsAsync(ct);
        Models = list.Where(m => m?["visibility"]?.ToString() == "list" && m?["slug"] is not null)
            .Select(m => new ModelChoice(m!["slug"]!.ToString(), m["display_name"]?.ToString() ?? m["slug"]!.ToString())).ToList(); Active = null;
    }
    public async Task ValidateAsync(ModelChoice model, CancellationToken ct)
    {
        var text = await responses.CompleteAsync(model.Slug, new JsonArray(ConversationState.Message("user", "Reply with exactly OK.")), "Be concise.", [], _ => Task.CompletedTask, ct);
        if (string.IsNullOrWhiteSpace(text.Text)) throw new InvalidDataException("Model text inference was empty.");
        var reasoning = text.Output.Any(i => i?["type"]?.ToString() == "reasoning");
        var vision = false; var tools = false;
        try
        {
            var image = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=";
            var response = await responses.CompleteAsync(model.Slug, new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = "Describe this tiny image briefly." }, new JsonObject { ["type"] = "input_image", ["image_url"] = image }) }), "Be concise.", [], _ => Task.CompletedTask, ct);
            vision = response.Text.Length > 0;
        }
        catch (SubscriptionException ex) when (ex.IsCapability) { }
        var definitions = new JsonArray(new JsonObject { ["type"] = "namespace", ["name"] = "capability", ["description"] = "Harmless capability test", ["tools"] = new JsonArray(new JsonObject { ["type"] = "function", ["name"] = "probe", ["description"] = "Return OK. This test doesn't act on the computer.", ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray(), ["additionalProperties"] = false }, ["strict"] = true }) });
        try
        {
            var input = new JsonArray(ConversationState.Message("user", "Call capability.probe with no arguments, then reply OK."));
            var response = await responses.CompleteAsync(model.Slug, input, "Perform the requested function call.", definitions, _ => Task.CompletedTask, ct);
            var call = response.Output.FirstOrDefault(i => i?["type"]?.ToString() == "function_call" && (i?["name"]?.ToString() is "probe" or "capability.probe"));
            if (call is not null)
            {
                ConversationState.AppendOutput(input, response.Output); ConversationState.AppendToolResult(input, call["call_id"]!.ToString(), new("OK"));
                var continued = await responses.CompleteAsync(model.Slug, input, "Reply OK after the tool result.", definitions, _ => Task.CompletedTask, ct);
                tools = continued.Text.Length > 0;
            }
        }
        catch (SubscriptionException ex) when (ex.IsCapability) { }
        model.Capabilities = new(true, vision, tools, reasoning);
        if (!vision || !tools) throw new InvalidOperationException("This model didn't validate vision and function calls. Choose another model.");
        Active = model;
    }
    public async Task ProbeWebAsync(CancellationToken ct)
    {
        var model = Active ?? throw new InvalidOperationException("Select and validate an AI model first.");
        try
        {
            var response = await responses.CompleteAsync(model.Slug, new JsonArray(ConversationState.Message("user", "Use web search to find OpenAI's official homepage; reply in one short sentence.")), "Use web_search for this capability test.", new JsonArray(new JsonObject { ["type"] = "web_search" }), _ => Task.CompletedTask, ct);
            var available = response.Output.Any(i => i?["type"]?.ToString() == "web_search_call");
            model.Capabilities = model.Capabilities! with { WebSearch = available };
        }
        catch (SubscriptionException ex) when (ex.IsCapability || ex.Status == System.Net.HttpStatusCode.Forbidden) { model.Capabilities = model.Capabilities! with { WebSearch = false }; }
    }
    public void DisableWeb() { if (Active?.Capabilities is { } caps) Active.Capabilities = caps with { WebSearch = false }; }
    public void Clear() { Active = null; Models = []; }
}
