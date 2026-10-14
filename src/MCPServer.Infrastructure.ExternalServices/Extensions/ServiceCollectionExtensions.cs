using MCPServer.BuildingBlock.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using MCPServer.Infrastructure.ExternalServices.Jira;
using MCPServer.Infrastructure.ExternalServices.Jira.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration.GetValue<JiraAuthMode?>(JiraOAuthOptions.AuthModeKey) ?? JiraAuthMode.ApiToken;
        return mode == JiraAuthMode.OAuth
            ? services.AddJiraOAuthAuthentication(configuration)
            : services.AddJiraApiTokenAuthentication(configuration);
    }

    /// <summary>Registers the OAuth 2.0 (3LO) facade; Jira calls carry the authorized caller's token only.</summary>
    public static IServiceCollection AddJiraOAuthAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JiraOAuthOptions>()
            .Bind(configuration.GetSection(JiraOAuthOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JiraOAuthOptions>, JiraOAuthOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OAuthStateStore>();
        services.AddSingleton<IJiraOAuthService, JiraOAuthService>();
        services.AddScoped<IJiraUserContext, JiraUserContext>();

        services.AddHttpClient<AtlassianOAuthClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MCPServer");
        });
        services.AddScoped<IJiraApiClient, JiraOAuthApiClient>();
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
