using System.ComponentModel;
using MCPServer.Application.Features.Jira.WhoAmI;
using ModelContextProtocol.Server;

namespace MCPServer.Api.Mcp;

[McpServerToolType]
public sealed class JiraMcpTools
{
    [McpServerTool(Name = "jira_whoami"), Description("Returns the Jira account the caller authorized via OAuth.")]
    public static Task<WhoAmIResult> WhoAmI(IWhoAmIService whoAmI, CancellationToken cancellationToken) =>
        whoAmI.GetAsync(cancellationToken);
}
