using MCPServer.Application.Features.Observability.Catalog;
using MCPServer.BuildingBlock.Interfaces;
using MCPServer.Application.Features.Observability.ConnectionStatus;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;

namespace MCPServer.Api.Tests;

public sealed class FakeConnectionStatusService : IConnectionStatusService
{
    public Func<Exception?> Failure { get; set; } = () => null;

    public Task<ConnectionStatusResult> GetAsync(CancellationToken cancellationToken)
        => Failure() is { } ex ? throw ex : Task.FromResult(new ConnectionStatusResult("test-cluster", "9.0.0", false, null));
}

public sealed class FakeCatalogService : ICatalogService
{
    public Task<ServiceCatalog> GetServicesAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ServiceCatalog(["checkout", "cart"], 2, false));

    public Task<FieldCatalog> GetFieldsAsync(string signal, CancellationToken cancellationToken)
        => Task.FromResult(new FieldCatalog(signal, [new CatalogField("@timestamp", "date")], 1, false));
}

public sealed class FakeElasticApiClient : IElasticApiClient
{
    public Func<LogSearchRequest, LogSearchResult> OnSearch { get; set; } = _ => new LogSearchResult([], 0);
    public Func<string, string, LogEntry?> OnGet { get; set; } = (_, _) => null;
    public Func<LogFilter, int, IReadOnlyList<LogHistogramBucket>> OnHistogram { get; set; } = (_, _) => [];
    public List<LogSearchRequest> Searches { get; } = [];
    public int HistogramInterval { get; private set; }

    public Task<ElasticClusterInfo> GetClusterInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new ElasticClusterInfo("test", "9.0.0"));
    public Task<IReadOnlyList<string>> ListServicesAsync(int max, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<ElasticField>> GetFieldsAsync(string signal, int max, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ElasticField>>([]);

    public Task<LogSearchResult> SearchLogsAsync(LogSearchRequest request, CancellationToken cancellationToken)
    {
        Searches.Add(request);
        return Task.FromResult(OnSearch(request));
    }

    public Task<LogEntry?> GetLogAsync(string index, string id, CancellationToken cancellationToken) => Task.FromResult(OnGet(index, id));

    public Task<IReadOnlyList<LogHistogramBucket>> GetLogHistogramAsync(LogFilter filter, int intervalSeconds, CancellationToken cancellationToken)
    {
        HistogramInterval = intervalSeconds;
        return Task.FromResult(OnHistogram(filter, intervalSeconds));
    }
}

public sealed class McpTestFactory : WebApplicationFactory<Program>
{
    public FakeConnectionStatusService Status { get; } = new();
    public FakeElasticApiClient Elastic { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elastic:Url"] = "http://localhost:9200",
            ["Elastic:ApiKey"] = "unused"
        }));
        builder.ConfigureServices(services =>
        {
            // No real Elastic during tests: drop the startup verifiers and use a fake service.
            foreach (var d in services.Where(d => d.ImplementationType?.Name.EndsWith("StartupVerifier") == true).ToList())
                services.Remove(d);
            services.RemoveAll<IConnectionStatusService>();
            services.AddSingleton<IConnectionStatusService>(Status);
            services.RemoveAll<IElasticApiClient>();
            services.AddSingleton<IElasticApiClient>(Elastic);
            services.RemoveAll<ICatalogService>();
            services.AddSingleton<ICatalogService>(new FakeCatalogService());
        });
    }

    public async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(Server.BaseAddress, "/mcp") },
            CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }
}
