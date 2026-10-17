using System.Net;
using System.Text;
using MCPServer.BuildingBlock.Interfaces;
using MCPServer.Infrastructure.ExternalServices.Elastic;
using MCPServer.Infrastructure.ExternalServices.Kibana;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Tests;

public class ElasticAuthenticationTests
{
    private const string Secret = "super-secret-key-123";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ElasticApiClient ElasticClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://es.example.com:9200/") });

    private static KibanaApiClient KibanaClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://kb.example.com:5601/") });

    private const string InfoBody = """{"cluster_name":"prod-obs","version":{"number":"8.15.1"}}""";

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // ---- OBS-AUTH-01 ----

    [Fact]
    public async Task GetClusterInfo_Success_ReturnsNameAndVersion()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, InfoBody));

        var info = await ElasticClient(handler).GetClusterInfoAsync(CancellationToken.None);

        Assert.Equal("prod-obs", info.ClusterName);
        Assert.Equal("8.15.1", info.Version);
        Assert.Equal("/", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetClusterInfo_FailureStatus_ThrowsSafeException(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json(status, $$"""{"error":"leak {{Secret}}"}"""));

        var ex = await Assert.ThrowsAnyAsync<ElasticAuthenticationException>(
            () => ElasticClient(handler).GetClusterInfoAsync(CancellationToken.None));

        Assert.DoesNotContain(Secret, ex.Message);
        Assert.DoesNotContain("leak", ex.Message);
    }

    [Fact]
    public async Task GetClusterInfo_NetworkFailure_ThrowsSafeException()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException($"boom {Secret}"));

        var ex = await Assert.ThrowsAnyAsync<ElasticAuthenticationException>(
            () => ElasticClient(handler).GetClusterInfoAsync(CancellationToken.None));

        Assert.DoesNotContain(Secret, ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task GetClusterInfo_MissingFields_Throws()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAnyAsync<ElasticAuthenticationException>(
            () => ElasticClient(handler).GetClusterInfoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Verifier_Success_LogsClusterAndVersion_NotKey()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, InfoBody));
        var logger = new CapturingLogger<ElasticStartupVerifier>();

        await new ElasticStartupVerifier(ElasticClient(handler), logger).StartAsync(CancellationToken.None);

        var line = Assert.Single(logger.Messages);
        Assert.Contains("prod-obs", line);
        Assert.Contains("8.15.1", line);
        Assert.DoesNotContain(Secret, line);
    }

    [Fact]
    public async Task Verifier_InvalidKey_FailsFast()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        var verifier = new ElasticStartupVerifier(ElasticClient(handler), new CapturingLogger<ElasticStartupVerifier>());

        await Assert.ThrowsAnyAsync<ElasticAuthenticationException>(() => verifier.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Di_ApiKey_SendsApiKeyAuthorizationHeader()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, InfoBody));
        using var provider = BuildProvider(handler, new()
        {
            ["Elastic:Url"] = "https://es.example.com:9200",
            ["Elastic:ApiKey"] = Secret
        });

        await provider.GetRequiredService<IElasticApiClient>().GetClusterInfoAsync(CancellationToken.None);

        Assert.Equal("ApiKey", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal(Secret, handler.Request.Headers.Authorization.Parameter);
    }

    private static ServiceProvider BuildProvider(StubHandler handler, Dictionary<string, string?> values)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddExternalServices(Config(values));
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider();
    }

    // ---- OBS-AUTH-02 ----

    private static string CloudId(string host = "us-east-1.aws.found.io", string es = "abc", string kb = "def") =>
        "my-deployment:" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{host}${es}${kb}"));

    [Fact]
    public void Options_ConflictingMethods_ReportsAllProblems_WithoutSecrets()
    {
        var result = new ElasticOptionsValidator().Validate(null, new ElasticOptions
        {
            Url = "http://es.example.com",
            CloudId = CloudId(),
            ApiKey = Secret,
            Username = "elastic",
            Password = "pw-secret",
            TimeoutSeconds = 0
        });

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
        Assert.DoesNotContain(Secret, result.FailureMessage);
        Assert.DoesNotContain("pw-secret", result.FailureMessage);
    }

    [Fact]
    public void Options_NothingConfigured_ReportsEndpointAndAuth()
    {
        var result = new ElasticOptionsValidator().Validate(null, new ElasticOptions());

        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Options_UsernameWithoutPassword_Fails()
    {
        var result = new ElasticOptionsValidator().Validate(null,
            new ElasticOptions { Url = "https://es.example.com", Username = "elastic" });

        Assert.Contains("together", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Options_ValidCombinations_Succeed()
    {
        var validator = new ElasticOptionsValidator();

        Assert.True(validator.Validate(null, new ElasticOptions { Url = "https://es.example.com", ApiKey = Secret }).Succeeded);
        Assert.True(validator.Validate(null, new ElasticOptions { Url = "http://localhost:9200", Username = "u", Password = "p" }).Succeeded);
        Assert.True(validator.Validate(null, new ElasticOptions { CloudId = CloudId(), ApiKey = Secret }).Succeeded);
    }

    [Fact]
    public void Options_MalformedCloudId_Fails()
    {
        var result = new ElasticOptionsValidator().Validate(null, new ElasticOptions { CloudId = "not-a-cloud-id", ApiKey = Secret });

        Assert.Contains("CloudId", Assert.Single(result.Failures!));
    }

    [Fact]
    public void CloudId_DecodesToElasticsearchEndpoint()
    {
        var uri = ElasticEndpoint.Resolve(new ElasticOptions { CloudId = CloudId() });

        Assert.Equal("https://abc.us-east-1.aws.found.io/", uri!.ToString());
    }

    [Fact]
    public async Task Di_BasicAuth_SendsBasicHeader()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, InfoBody));
        using var provider = BuildProvider(handler, new()
        {
            ["Elastic:CloudId"] = CloudId(),
            ["Elastic:Username"] = "elastic",
            ["Elastic:Password"] = "pw"
        });

        await provider.GetRequiredService<IElasticApiClient>().GetClusterInfoAsync(CancellationToken.None);

        Assert.Equal("Basic", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("abc.us-east-1.aws.found.io", handler.Request.RequestUri!.Host);
    }

    [Fact]
    public void Di_MissingConfiguration_FailsOptionsValidation_WithoutSecret()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddExternalServices(Config(new() { ["Elastic:ApiKey"] = Secret, ["Elastic:Username"] = "u" }));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ElasticOptions>>().Value);

        Assert.DoesNotContain(Secret, ex.Message);
        Assert.Contains("Elastic:Url", ex.Message);
        Assert.Contains("exactly one authentication method", ex.Message);
    }

    // ---- OBS-AUTH-03 ----

    [Fact]
    public async Task Kibana_Status_ReturnsVersion_AndCallsStatusEndpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"version":{"number":"8.15.1"}}"""));

        var status = await KibanaClient(handler).GetStatusAsync(CancellationToken.None);

        Assert.Equal("8.15.1", status.Version);
        Assert.Equal("/api/status", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Kibana_FailureStatus_ThrowsSafeException(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json(status, $$"""{"message":"leak {{Secret}}"}"""));

        var ex = await Assert.ThrowsAnyAsync<KibanaConnectionException>(
            () => KibanaClient(handler).GetStatusAsync(CancellationToken.None));

        Assert.DoesNotContain(Secret, ex.Message);
    }

    [Fact]
    public async Task KibanaVerifier_LogsVersion()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"version":{"number":"8.15.1"}}"""));
        var logger = new CapturingLogger<KibanaStartupVerifier>();

        await new KibanaStartupVerifier(KibanaClient(handler), logger).StartAsync(CancellationToken.None);

        Assert.Contains("8.15.1", Assert.Single(logger.Messages));
    }

    [Fact]
    public void Di_KibanaNotConfigured_IsNotRegistered_ElasticStillWorks()
    {
        using var provider = BuildProvider(new StubHandler(_ => Json(HttpStatusCode.OK, InfoBody)), new()
        {
            ["Elastic:Url"] = "https://es.example.com:9200",
            ["Elastic:ApiKey"] = Secret
        });

        Assert.NotNull(provider.GetService<IElasticApiClient>());
        Assert.Null(provider.GetService<IKibanaApiClient>());
        Assert.DoesNotContain(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is KibanaStartupVerifier);
    }

    [Fact]
    public async Task Di_KibanaConfigured_FallsBackToElasticCredentials()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"version":{"number":"8.15.1"}}"""));
        using var provider = BuildProvider(handler, new()
        {
            ["Elastic:Url"] = "https://es.example.com:9200",
            ["Elastic:ApiKey"] = Secret,
            ["Kibana:Url"] = "https://kb.example.com:5601"
        });

        await provider.GetRequiredService<IKibanaApiClient>().GetStatusAsync(CancellationToken.None);

        Assert.Equal("ApiKey", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("kb.example.com", handler.Request.RequestUri!.Host);
        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is KibanaStartupVerifier);
    }

    [Fact]
    public void Kibana_InvalidOptions_ReportsAllProblems()
    {
        var result = new KibanaOptionsValidator().Validate(null,
            new KibanaOptions { Url = "http://kb.example.com", ApiKey = Secret, Username = "u", TimeoutSeconds = 999 });

        Assert.Equal(3, result.Failures!.Count());
        Assert.DoesNotContain(Secret, result.FailureMessage);
    }
}
