using MCPServer.BuildingBlock.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.Application.Features.Observability.ConnectionStatus;

public sealed record ConnectionStatusResult(string ClusterName, string ElasticVersion, bool KibanaConfigured, string? KibanaVersion);

public interface IConnectionStatusService
{
    Task<ConnectionStatusResult> GetAsync(CancellationToken cancellationToken);
}

public sealed class ConnectionStatusService(IElasticApiClient elastic, IServiceProvider services) : IConnectionStatusService
{
    public async Task<ConnectionStatusResult> GetAsync(CancellationToken cancellationToken)
    {
        var cluster = await elastic.GetClusterInfoAsync(cancellationToken);
        var kibana = services.GetService<IKibanaApiClient>();
        var kibanaStatus = kibana is null ? null : await kibana.GetStatusAsync(cancellationToken);
        return new ConnectionStatusResult(cluster.ClusterName, cluster.Version, kibana is not null, kibanaStatus?.Version);
    }
}
