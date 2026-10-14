using MCPServer.BuildingBlock.Interfaces;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

/// <summary>Calls Jira on behalf of the current caller, using only that caller's access token.</summary>
public sealed class JiraOAuthApiClient(
    AtlassianOAuthClient atlassian,
    IJiraUserContext user) : IJiraApiClient
{
    public async Task<JiraMyself> GetMyselfAsync(CancellationToken cancellationToken)
    {
        var session = user.Current
            ?? throw new JiraAuthenticationException(code: "401", message: "No authorized Jira user for this request.");

        var me = await atlassian.GetMyselfAsync(session.AccessToken, session.CloudId, cancellationToken);
        return new JiraMyself(me.AccountId!, me.DisplayName);
    }
}
