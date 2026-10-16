using MCPServer.Infrastructure.ExternalServices.Elastic;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Kibana;

/// <summary>Validates <see cref="KibanaOptions"/>, reporting every problem at once. Never echoes secrets.</summary>
public sealed class KibanaOptionsValidator : IValidateOptions<KibanaOptions>
{
    public ValidateOptionsResult Validate(string? name, KibanaOptions options)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri))
        {
            errors.Add("Kibana:Url must be an absolute URL, e.g. https://my-kibana.example.com:5601.");
        }
        else if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            errors.Add("Kibana:Url must use https (http is only allowed for localhost).");
        }

        var hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        var hasUsername = !string.IsNullOrWhiteSpace(options.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(options.Password);
        if (hasApiKey && (hasUsername || hasPassword))
        {
            errors.Add("Kibana:ApiKey and Kibana:Username/Password are both set; configure exactly one authentication method.");
        }
        else if (!hasApiKey && hasUsername != hasPassword)
        {
            errors.Add("Kibana:Username and Kibana:Password must be provided together.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Kibana:TimeoutSeconds must be between 1 and 300.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
