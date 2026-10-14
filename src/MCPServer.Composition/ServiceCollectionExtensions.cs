using MCPServer.Application.Extensions;
using MCPServer.Infrastructure.ExternalServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.Composition;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCompositionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddExternalServices(configuration);
        services.AddApplicationServices();

        return services;
    }
}