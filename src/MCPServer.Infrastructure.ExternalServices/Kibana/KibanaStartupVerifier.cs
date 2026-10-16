using MCPServer.BuildingBlock.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.ExternalServices.Kibana;

/// <summary>Verifies Kibana connectivity at startup; only registered when Kibana is configured.</summary>
public sealed class KibanaStartupVerifier(IKibanaApiClient client, ILogger<KibanaStartupVerifier> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var status = await client.GetStatusAsync(cancellationToken);
        logger.LogInformation("Connected to Kibana version {Version}", status.Version);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
