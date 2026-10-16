using System.Net.Http.Headers;
using System.Text;
using MCPServer.BuildingBlock.Interfaces;
using MCPServer.Infrastructure.ExternalServices.Elastic;
using MCPServer.Infrastructure.ExternalServices.Kibana;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
        => services.AddElasticAuthentication(configuration);

    /// <summary>Registers Elasticsearch (API key or basic auth) and, only when <c>Kibana:Url</c> is set, Kibana.</summary>
    public static IServiceCollection AddElasticAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ElasticOptions>()
            .Bind(configuration.GetSection(ElasticOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ElasticOptions>, ElasticOptionsValidator>());

        services.AddHttpClient<IElasticApiClient, ElasticApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
            var endpoint = ElasticEndpoint.Resolve(options)!;
            client.BaseAddress = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = CreateAuthorization(options.ApiKey, options.Username, options.Password);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MCPServer");
        });
        services.AddHostedService<ElasticStartupVerifier>();

        if (!string.IsNullOrWhiteSpace(configuration[$"{KibanaOptions.SectionName}:Url"]))
        {
            services.AddKibanaAuthentication(configuration);
        }

        return services;
    }

    private static IServiceCollection AddKibanaAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KibanaOptions>()
            .Bind(configuration.GetSection(KibanaOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KibanaOptions>, KibanaOptionsValidator>());

        services.AddHttpClient<IKibanaApiClient, KibanaApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<KibanaOptions>>().Value;
            var elastic = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
            var ownCredentials = !string.IsNullOrWhiteSpace(options.ApiKey) || !string.IsNullOrWhiteSpace(options.Username);
            client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = ownCredentials
                ? CreateAuthorization(options.ApiKey, options.Username, options.Password)
                : CreateAuthorization(elastic.ApiKey, elastic.Username, elastic.Password);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MCPServer");
        });
        services.AddHostedService<KibanaStartupVerifier>();
        return services;
    }

    private static AuthenticationHeaderValue CreateAuthorization(string apiKey, string username, string password)
        => !string.IsNullOrWhiteSpace(apiKey)
            ? new AuthenticationHeaderValue("ApiKey", apiKey)
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
}
