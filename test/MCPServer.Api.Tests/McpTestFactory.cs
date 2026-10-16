using MCPServer.Application.Features.Observability.Catalog;
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

public sealed class McpTestFactory : WebApplicationFactory<Program>
{
    public FakeConnectionStatusService Status { get; } = new();

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
