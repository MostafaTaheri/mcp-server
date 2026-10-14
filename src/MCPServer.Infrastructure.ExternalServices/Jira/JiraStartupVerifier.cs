using MCPServer.BuildingBlock.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.ExternalServices.Jira;

/// <summary>Verifies the configured credentials at startup and aborts the host if they are invalid.</summary>
public sealed class JiraStartupVerifier(IJiraApiClient client, ILogger<JiraStartupVerifier> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await client.GetMyselfAsync(cancellationToken);
        logger.LogInformation("Connected to Jira as account {AccountId}", me.AccountId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
