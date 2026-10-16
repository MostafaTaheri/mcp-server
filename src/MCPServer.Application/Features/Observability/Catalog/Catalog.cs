using System.Collections.Concurrent;
using MCPServer.Application.Mcp;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Application.Features.Observability.Catalog;

public sealed record CatalogField(string Name, string Type);

public sealed record ServiceCatalog(IReadOnlyList<string> Services, int Count, bool Truncated);

public sealed record FieldCatalog(string Signal, IReadOnlyList<CatalogField> Fields, int Count, bool Truncated);

public interface ICatalogService
{
    Task<ServiceCatalog> GetServicesAsync(CancellationToken cancellationToken);

    Task<FieldCatalog> GetFieldsAsync(string signal, CancellationToken cancellationToken);
}

/// <summary>Shared across requests so catalogs are fetched from Elastic at most once per <see cref="Ttl"/>.</summary>
public sealed class CatalogCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, object Value)> entries = new();

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory) where T : class
    {
        if (entries.TryGetValue(key, out var hit) && hit.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return (T)hit.Value;
        }

        var value = await factory();
        entries[key] = (DateTimeOffset.UtcNow + Ttl, value);
        return value;
    }
}

public sealed class CatalogService(IElasticApiClient elastic, CatalogCache cache) : ICatalogService
{
    public const int MaxServices = 200;
    public const int MaxFields = 1000;

    public static readonly IReadOnlyList<string> Signals = ["logs", "traces", "metrics"];

    public Task<ServiceCatalog> GetServicesAsync(CancellationToken cancellationToken)
        => cache.GetOrAddAsync("services", async () =>
        {
            // Ask for one extra so we can tell whether the list was cut.
            var names = await elastic.ListServicesAsync(MaxServices + 1, cancellationToken);
            var truncated = names.Count > MaxServices;
            var services = names.Take(MaxServices).ToList();
            return new ServiceCatalog(services, services.Count, truncated);
        });

    public Task<FieldCatalog> GetFieldsAsync(string signal, CancellationToken cancellationToken)
    {
        if (!Signals.Contains(signal))
        {
            throw new ToolException(ToolErrorCode.InvalidArgument,
                "Unknown signal.",
                "Use one of: logs, traces, metrics.");
        }

        return cache.GetOrAddAsync($"fields:{signal}", async () =>
        {
            var fields = await elastic.GetFieldsAsync(signal, MaxFields + 1, cancellationToken);
            var truncated = fields.Count > MaxFields;
            var kept = fields.Take(MaxFields).Select(f => new CatalogField(f.Name, f.Type)).ToList();
            return new FieldCatalog(signal, kept, kept.Count, truncated);
        });
    }
}
