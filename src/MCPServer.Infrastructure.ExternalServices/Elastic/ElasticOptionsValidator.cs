using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

/// <summary>Validates <see cref="ElasticOptions"/>, reporting every problem at once. Never echoes secrets.</summary>
public sealed class ElasticOptionsValidator : IValidateOptions<ElasticOptions>
{
    public ValidateOptionsResult Validate(string? name, ElasticOptions options)
    {
        var errors = new List<string>();

        var hasUrl = !string.IsNullOrWhiteSpace(options.Url);
        var hasCloudId = !string.IsNullOrWhiteSpace(options.CloudId);
        if (hasUrl == hasCloudId)
        {
            errors.Add(hasUrl
                ? "Elastic:Url and Elastic:CloudId are both set; configure exactly one."
                : "Elastic:Url or Elastic:CloudId must be provided.");
        }
        else if (hasUrl)
        {
            if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri))
            {
                errors.Add("Elastic:Url must be an absolute URL, e.g. https://my-cluster.example.com:9200.");
            }
            else if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            {
                errors.Add("Elastic:Url must use https (http is only allowed for localhost).");
            }
        }
        else if (!ElasticEndpoint.TryDecodeCloudId(options.CloudId, out _))
        {
            errors.Add("Elastic:CloudId is malformed; expected the value shown in the Elastic Cloud console.");
        }

        var hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        var hasUsername = !string.IsNullOrWhiteSpace(options.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(options.Password);
        if (hasApiKey && (hasUsername || hasPassword))
        {
            errors.Add("Elastic:ApiKey and Elastic:Username/Password are both set; configure exactly one authentication method.");
        }
        else if (!hasApiKey && !hasUsername && !hasPassword)
        {
            errors.Add("An authentication method must be provided: set Elastic__ApiKey, or Elastic__Username and Elastic__Password.");
        }
        else if (!hasApiKey && hasUsername != hasPassword)
        {
            errors.Add("Elastic:Username and Elastic:Password must be provided together.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Elastic:TimeoutSeconds must be between 1 and 300.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
