using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Application.Features.Jira.WhoAmI;

public sealed class WhoAmIService(IJiraApiClient jira) : IWhoAmIService
{
    public async Task<WhoAmIResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var me = await jira.GetMyselfAsync(cancellationToken);
        return new WhoAmIResult(me.AccountId, me.DisplayName);
    }
}
