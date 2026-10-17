using System.ComponentModel;
using MCPServer.Application.Features.Observability.ConnectionStatus;
using ModelContextProtocol.Server;

namespace MCPServer.Api.Mcp;

[McpServerToolType]
public sealed class ObservabilityTools(IConnectionStatusService connectionStatus)
{
    [McpServerTool(Name = "obs_get_connection_status", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Checks that the server can reach Elasticsearch (and Kibana, if configured) and returns the cluster name and versions. Takes no arguments, returns a single small object, and never returns credentials or log data. Call it first to confirm connectivity before searching logs or traces.")]
    public Task<ConnectionStatusResult> GetConnectionStatus(CancellationToken cancellationToken)
        => connectionStatus.GetAsync(cancellationToken);
}
