using System.Net;
using System.Text.Json.Nodes;
namespace GooglyWindows.Core.AI;
public sealed class SubscriptionException(string code, HttpStatusCode? status = null, string? parameter = null, string? requestID = null) : Exception(MessageFor(code))
{
    public string Code { get; } = code;
    public HttpStatusCode? Status { get; } = status;
    public string? Parameter { get; } = parameter;
    public string? RequestID { get; } = requestID;
    public bool IsCapability => Code is "subscription_sharing_unsupported_capability" or "subscription_sharing_route_not_supported" or "model_not_found" or "permission_denied";
    public bool IsTransient => Code is "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" || (int?)Status >= 500;
    public static SubscriptionException Parse(JsonNode? body, HttpStatusCode? status, string? requestID = null)
    {
        var error = body?["error"] ?? body?["response"]?["error"] ?? body;
        return new(error?["code"]?.GetValue<string>() ?? (status == HttpStatusCode.Unauthorized ? "sign_in_required" : "request_failed"), status, error?["param"]?.ToString(), requestID);
    }
    public static string MessageFor(string code) => code switch
    {
        "subscription_sharing_usage_limit_exceeded" => "ChatGPT plan usage limit reached. Requests are paused. Review ChatGPT Settings → Usage, then choose Resume in Settings.",
        "subscription_sharing_user_not_eligible" => "ChatGPT plan usage isn't available for this account or workspace.",
        "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" => "ChatGPT plan usage is temporarily unavailable. Try again shortly.",
        "subscription_sharing_unsupported_capability" => "This capability isn't supported by the connected ChatGPT plan.",
        "subscription_sharing_route_not_supported" => "This route isn't supported by ChatGPT plan usage.",
        "subscription_sharing_invalid_user" or "sign_in_required" or "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused" => "Sign in to ChatGPT again.",
        "chatpass_v2_scope_not_authorized" or "chatpass_v2_invalid_authorization_context" => "ChatGPT plan permission wasn't authorized. Reconnect and grant plan usage.",
        "stream_interrupted" => "The response was interrupted before completion. Please ask again.",
        "response_incomplete" => "ChatGPT returned an incomplete response. Please ask again.",
        _ => "ChatGPT couldn't complete this request. Check the account and connection."
    };
}
public interface IAccessTokenProvider { Task<string> GetAccessTokenAsync(CancellationToken ct); }
