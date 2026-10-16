using MCPServer.BuildingBlock.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

/// <summary>Verifies the configured credentials at startup and aborts the host if they are invalid.</summary>
public sealed class ElasticStartupVerifier(IElasticApiClient client, ILogger<ElasticStartupVerifier> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var info = await client.GetClusterInfoAsync(cancellationToken);
        logger.LogInformation("Connected to Elasticsearch cluster {ClusterName} version {Version}", info.ClusterName, info.Version);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
