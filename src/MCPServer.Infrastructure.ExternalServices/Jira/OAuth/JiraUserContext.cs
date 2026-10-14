using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

public sealed class JiraUserContext : IJiraUserContext
{
    public JiraOAuthSession? Current { get; set; }
}
