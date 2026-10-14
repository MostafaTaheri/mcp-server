using MCPServer.BuildingBlock.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using MCPServer.Infrastructure.ExternalServices.Jira;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddJiraApiTokenAuthentication(configuration);
        return services;
    }

    public static IServiceCollection AddJiraApiTokenAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JiraOptions>()
            .Bind(configuration.GetSection(JiraOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JiraOptions>, JiraOptionsValidator>());

        services.AddHttpClient<IJiraApiClient, JiraApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
            client.BaseAddress = new Uri(options.SiteUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Email}:{options.ApiToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MCPServer");
        });

        services.AddHostedService<JiraStartupVerifier>();
        return services;
    }
}
