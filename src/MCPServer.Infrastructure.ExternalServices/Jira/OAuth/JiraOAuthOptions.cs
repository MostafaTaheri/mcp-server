namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

public enum JiraAuthMode
{
    ApiToken,
    OAuth
}

public sealed class JiraOAuthOptions
{
    public const string SectionName = "Jira:OAuth";
    public const string AuthModeKey = "Jira:AuthMode";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Public base URL of this server (used for the callback and the metadata documents).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = ["read:jira-work", "read:jira-user"];

    public int SessionLifetimeMinutes { get; set; } = 55;

    public string AuthorizeUrl { get; set; } = "https://auth.atlassian.com/authorize";

    public string TokenUrl { get; set; } = "https://auth.atlassian.com/oauth/token";

    public string ApiBaseUrl { get; set; } = "https://api.atlassian.com/";

    public string CallbackPath { get; set; } = "/oauth/callback";
}
