using MCPServer.Application.Features.Observability.Catalog;
using MCPServer.Application.Features.Observability.ConnectionStatus;
using MCPServer.Application.Features.Observability.Logs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MCPServer.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IConnectionStatusService, ConnectionStatusService>();
        services.AddSingleton<CatalogCache>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ILogTextSanitizer, PassThroughLogTextSanitizer>();
        services.AddScoped<ILogSearchService, LogSearchService>();
        return services;
    }
}
