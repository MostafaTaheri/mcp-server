using System.Net;
using System.Text;
using MCPServer.Infrastructure.ExternalServices;
using MCPServer.Infrastructure.ExternalServices.Jira;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Tests;

public class JiraAuthenticationTests
{
    private const string Token = "super-secret-token-123";

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

    private static JiraApiClient CreateClient(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net/") };
        http.DefaultRequestHeaders.Authorization = new("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"me@example.com:{Token}")));
        return new JiraApiClient(http);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetMyself_Success_ReturnsAccountId_AndCallsMyselfEndpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"accountId":"abc123","displayName":"Mo"}"""));

        var me = await CreateClient(handler).GetMyselfAsync(CancellationToken.None);

        Assert.Equal("abc123", me.AccountId);
        Assert.Equal("/rest/api/3/myself", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetMyself_FailureStatus_ThrowsSafeException(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json(status, $$"""{"message":"leak {{Token}}"}"""));

        var ex = await Assert.ThrowsAnyAsync<JiraAuthenticationException>(
            () => CreateClient(handler).GetMyselfAsync(CancellationToken.None));

        Assert.DoesNotContain(Token, ex.Message);
        Assert.DoesNotContain("leak", ex.Message);
    }

    [Fact]
    public async Task GetMyself_NetworkFailure_ThrowsSafeException()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException($"boom {Token}"));

        var ex = await Assert.ThrowsAnyAsync<JiraAuthenticationException>(
            () => CreateClient(handler).GetMyselfAsync(CancellationToken.None));

        Assert.DoesNotContain(Token, ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task GetMyself_MissingAccountId_Throws()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAnyAsync<JiraAuthenticationException>(
            () => CreateClient(handler).GetMyselfAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Verifier_Success_LogsAccountId_NotToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"accountId":"abc123"}"""));
        var logger = new CapturingLogger<JiraStartupVerifier>();

        await new JiraStartupVerifier(CreateClient(handler), logger).StartAsync(CancellationToken.None);

        var line = Assert.Single(logger.Messages);
        Assert.Contains("abc123", line);
        Assert.DoesNotContain(Token, line);
    }

    [Fact]
    public async Task Verifier_InvalidCredentials_FailsFast()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        var verifier = new JiraStartupVerifier(CreateClient(handler), new CapturingLogger<JiraStartupVerifier>());

        await Assert.ThrowsAnyAsync<JiraAuthenticationException>(() => verifier.StartAsync(CancellationToken.None));
    }

    [Fact]
    public void Options_Invalid_ReportsAllProblems_WithoutToken()
    {
        var result = new JiraOptionsValidator().Validate(null,
            new JiraOptions { SiteUrl = "http://example.atlassian.net", Email = "nope", ApiToken = "" });

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void Options_Valid_Succeeds_AndAllowsLocalhostHttp()
    {
        var validator = new JiraOptionsValidator();

        Assert.True(validator.Validate(null,
            new JiraOptions { SiteUrl = "https://x.atlassian.net", Email = "a@b.com", ApiToken = Token }).Succeeded);
        Assert.True(validator.Validate(null,
            new JiraOptions { SiteUrl = "http://localhost:8080", Email = "a@b.com", ApiToken = Token }).Succeeded);
    }

    [Fact]
    public void Di_MissingConfiguration_FailsOptionsValidation_WithoutToken()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jira:ApiToken"] = Token })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddExternalServices(config);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JiraOptions>>().Value);

        Assert.DoesNotContain(Token, ex.Message);
        Assert.Contains("Jira:SiteUrl", ex.Message);
        Assert.Contains("Jira:Email", ex.Message);
    }
}
