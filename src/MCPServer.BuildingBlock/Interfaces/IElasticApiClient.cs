namespace MCPServer.BuildingBlock.Interfaces;

public sealed record ElasticClusterInfo(string ClusterName, string Version);

public sealed record ElasticField(string Name, string Type);

public interface IElasticApiClient
{
    /// <summary>
    /// Returns cluster name and version via <c>GET /</c>.
    /// Throws when the credentials are rejected or Elasticsearch is unreachable.
    /// </summary>
    Task<ElasticClusterInfo> GetClusterInfoAsync(CancellationToken cancellationToken);

    /// <summary>Distinct <c>service.name</c> values across logs, traces and metrics, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<string>> ListServicesAsync(int max, CancellationToken cancellationToken);

    /// <summary>Field names and types for a signal (<c>logs</c>, <c>traces</c>, <c>metrics</c>), at most <paramref name="max"/>, sorted by name.</summary>
    Task<IReadOnlyList<ElasticField>> GetFieldsAsync(string signal, int max, CancellationToken cancellationToken);
}
