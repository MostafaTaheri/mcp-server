using System.ComponentModel;
using System.Text.Json;
using MCPServer.Application.Features.Observability.Catalog;
using ModelContextProtocol.Server;

namespace MCPServer.Api.Mcp;

[McpServerResourceType]
public sealed class ObservabilityResources(ICatalogService catalog)
{
    [McpServerResource(UriTemplate = "obs://services", Name = "services", MimeType = "application/json")]
    [Description("Service names (service.name) seen in logs, traces and metrics. At most 200, sorted, cached for 5 minutes; 'truncated' says whether the list was cut.")]
    public async Task<string> Services(CancellationToken cancellationToken)
        => JsonSerializer.Serialize(await catalog.GetServicesAsync(cancellationToken), JsonSerializerOptions.Web);

    [McpServerResource(UriTemplate = "obs://fields/{signal}", Name = "fields", MimeType = "application/json")]
    [Description("Field names and types available for a signal: logs, traces or metrics. At most 1000 fields, sorted, cached for 5 minutes; 'truncated' says whether the list was cut.")]
    public async Task<string> Fields([Description("logs, traces or metrics")] string signal, CancellationToken cancellationToken)
        => JsonSerializer.Serialize(await catalog.GetFieldsAsync(signal, cancellationToken), JsonSerializerOptions.Web);
}
