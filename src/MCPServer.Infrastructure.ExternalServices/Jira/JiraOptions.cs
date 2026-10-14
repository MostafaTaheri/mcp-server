using System.ComponentModel.DataAnnotations;

namespace MCPServer.Infrastructure.ExternalServices.Jira;

public sealed class JiraOptions
{
    public const string SectionName = "Jira";

    public string SiteUrl { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
