namespace MCPServer.BuildingBlock.Interfaces;

public sealed record KibanaStatus(string Version);

public interface IKibanaApiClient
{
    /// <summary>
    /// Returns the Kibana version via <c>GET /api/status</c>.
    /// Throws when the credentials are rejected or Kibana is unreachable.
    /// </summary>
    Task<KibanaStatus> GetStatusAsync(CancellationToken cancellationToken);
}
