using System.ComponentModel.DataAnnotations;

namespace MCPServer.Infrastructure.ExternalServices.Kibana;

/// <summary>Kibana is optional: it is only registered when <see cref="Url"/> is set. Unset credentials fall back to the Elastic ones.</summary>
public sealed class KibanaOptions
{
    public const string SectionName = "Kibana";

    public string Url { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
