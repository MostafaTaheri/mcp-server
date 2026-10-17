namespace MCPServer.Application.Mcp;

public enum ToolErrorCode
{
    InvalidArgument,
    NotFound,
    Forbidden,
    AuthRequired,
    RateLimited,
    UpstreamUnavailable,
    Timeout,
    CursorExpired
}

/// <summary>Agent-facing error: a stable code, a safe message and a suggested next step. Never carries credentials, queries or upstream internals.</summary>
public sealed record ToolError(string Code, string Message, string NextStep)
{
    public static ToolError From(ToolErrorCode code, string message, string nextStep)
        => new(ToCode(code), message, nextStep);

    public static string ToCode(ToolErrorCode code) => code switch
    {
        ToolErrorCode.InvalidArgument => "invalid_argument",
        ToolErrorCode.NotFound => "not_found",
        ToolErrorCode.Forbidden => "forbidden",
        ToolErrorCode.AuthRequired => "auth_required",
        ToolErrorCode.RateLimited => "rate_limited",
        ToolErrorCode.UpstreamUnavailable => "upstream_unavailable",
        ToolErrorCode.Timeout => "timeout",
        ToolErrorCode.CursorExpired => "cursor_expired",
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    };
}

/// <summary>Intentional tool failure. Message and next step must be fixed strings, never built from upstream text or user input.</summary>
public sealed class ToolException(ToolErrorCode code, string safeMessage, string nextStep) : Exception(safeMessage)
{
    public ToolError Error { get; } = ToolError.From(code, safeMessage, nextStep);
}
