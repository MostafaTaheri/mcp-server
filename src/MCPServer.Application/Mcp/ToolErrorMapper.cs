using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Application.Mcp;

public static class ToolErrorMapper
{
    public static ToolError Map(Exception exception) => exception switch
    {
        ToolException tool => tool.Error,
        IUpstreamFailure upstream => FromUpstream(upstream.Kind),
        TimeoutException or TaskCanceledException => FromUpstream(UpstreamFailureKind.Timeout),
        _ => FromUpstream(UpstreamFailureKind.Unavailable)
    };

    private static ToolError FromUpstream(UpstreamFailureKind kind) => kind switch
    {
        UpstreamFailureKind.Unauthorized => ToolError.From(ToolErrorCode.AuthRequired,
            "The server's credentials for Elastic were rejected.",
            "Ask the operator to check the Elastic authentication settings."),
        UpstreamFailureKind.Forbidden => ToolError.From(ToolErrorCode.Forbidden,
            "The server's credentials are not allowed to read this data.",
            "Ask the operator to grant read access, or try a different scope."),
        UpstreamFailureKind.NotFound => ToolError.From(ToolErrorCode.NotFound,
            "The requested data was not found.",
            "Check the identifier or time range and try again."),
        UpstreamFailureKind.RateLimited => ToolError.From(ToolErrorCode.RateLimited,
            "Too many requests were sent to Elastic.",
            "Wait a few seconds, then retry with a narrower request."),
        UpstreamFailureKind.Timeout => ToolError.From(ToolErrorCode.Timeout,
            "The request took too long.",
            "Retry with a narrower time range or fewer results."),
        _ => ToolError.From(ToolErrorCode.UpstreamUnavailable,
            "Elastic or Kibana could not be reached.",
            "Retry shortly; if it persists, ask the operator to check connectivity.")
    };
}
