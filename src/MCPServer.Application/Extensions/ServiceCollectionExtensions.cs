using MCPServer.Application.Features.Observability.Catalog;
using MCPServer.Application.Features.Observability.ConnectionStatus;
using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IConnectionStatusService, ConnectionStatusService>();
        services.AddSingleton<CatalogCache>();
        services.AddScoped<ICatalogService, CatalogService>();
        return services;
    }
}
