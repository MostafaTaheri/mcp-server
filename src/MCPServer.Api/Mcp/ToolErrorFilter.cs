using System.Text.Json;
using MCPServer.Application.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPServer.Api.Mcp;

public static class ToolErrorFilter
{
    /// <summary>Turns any exception from a tool call into a structured <c>isError</c> result; the real exception is only logged.</summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create()
    {
        return next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var logger = context.Services!.GetRequiredService<ILoggerFactory>().CreateLogger("MCPServer.Api.Mcp.ToolErrors");
                var error = ToolErrorMapper.Map(ex);
                logger.LogWarning(ex, "Tool {Tool} failed with {Code}", context.Params?.Name, error.Code);
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = $"{error.Code}: {error.Message} Next: {error.NextStep}" }],
                    StructuredContent = JsonSerializer.SerializeToElement(error, JsonSerializerOptions.Web)
                };
            }
        };
    }
}
