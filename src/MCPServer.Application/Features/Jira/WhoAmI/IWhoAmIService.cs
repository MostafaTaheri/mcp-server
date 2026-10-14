namespace MCPServer.Application.Features.Jira.WhoAmI;

public interface IWhoAmIService
{
    /// <summary>Returns the Jira account the current caller is authorized as.</summary>
    Task<WhoAmIResult> GetAsync(CancellationToken cancellationToken = default);
}
