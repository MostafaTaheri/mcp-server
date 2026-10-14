using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

/// <summary>Validates <see cref="JiraOAuthOptions"/>, reporting every problem at once. Never echoes the secret.</summary>
public sealed class JiraOAuthOptionsValidator : IValidateOptions<JiraOAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, JiraOAuthOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            errors.Add("Jira:OAuth:ClientId must be provided (the client ID of your Atlassian OAuth 2.0 (3LO) app).");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            errors.Add("Jira:OAuth:ClientSecret must be provided (set the Jira__OAuth__ClientSecret environment variable).");
        }

        if (!IsSecureOrLoopback(options.PublicBaseUrl))
        {
            errors.Add("Jira:OAuth:PublicBaseUrl must be an absolute https URL (http is only allowed for localhost).");
        }

        if (options.Scopes.Length == 0 || options.Scopes.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Jira:OAuth:Scopes must contain at least one scope.");
        }

        if (options.SessionLifetimeMinutes is < 1 or > 1440)
        {
            errors.Add("Jira:OAuth:SessionLifetimeMinutes must be between 1 and 1440.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsSecureOrLoopback(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
