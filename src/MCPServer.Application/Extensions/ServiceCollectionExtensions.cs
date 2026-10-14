using MCPServer.Application.Features.Jira.OAuth;
using MCPServer.Application.Features.Jira.WhoAmI;
using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IWhoAmIService, WhoAmIService>();
        services.AddScoped<IJiraAuthorizationService, JiraAuthorizationService>();
        return services;
    }
}
