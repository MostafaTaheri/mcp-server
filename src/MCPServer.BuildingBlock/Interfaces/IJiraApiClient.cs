namespace MCPServer.BuildingBlock.Interfaces;

public sealed record JiraMyself(string AccountId, string? DisplayName);

public interface IJiraApiClient
{
    /// <summary>
    /// Returns the authenticated user via <c>GET /rest/api/3/myself</c>.
    /// Throws when the credentials are rejected or Jira is unreachable.
    /// </summary>
    Task<JiraMyself> GetMyselfAsync(CancellationToken cancellationToken);
}
