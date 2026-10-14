using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Jira;

/// <summary>Validates <see cref="JiraOptions"/>, reporting every problem at once. Never echoes the token.</summary>
public sealed class JiraOptionsValidator : IValidateOptions<JiraOptions>
{
    public ValidateOptionsResult Validate(string? name, JiraOptions options)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(options.SiteUrl, UriKind.Absolute, out var siteUri))
        {
            errors.Add("Jira:SiteUrl must be an absolute URL, e.g. https://your-site.atlassian.net.");
        }
        else if (siteUri.Scheme != Uri.UriSchemeHttps && !(siteUri.Scheme == Uri.UriSchemeHttp && siteUri.IsLoopback))
        {
            errors.Add("Jira:SiteUrl must use https (http is only allowed for localhost).");
        }

        if (string.IsNullOrWhiteSpace(options.Email) || !MailAddress.TryCreate(options.Email, out _))
        {
            errors.Add("Jira:Email must be a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            errors.Add("Jira:ApiToken must be provided (set the Jira__ApiToken environment variable).");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Jira:TimeoutSeconds must be between 1 and 300.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
