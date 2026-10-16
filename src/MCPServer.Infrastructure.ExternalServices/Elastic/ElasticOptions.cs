using System.ComponentModel.DataAnnotations;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

public sealed class ElasticOptions
{
    public const string SectionName = "Elastic";

    public string Url { get; set; } = string.Empty;

    public string CloudId { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
