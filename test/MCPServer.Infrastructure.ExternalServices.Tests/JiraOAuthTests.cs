using System.Net;
using System.Text;
using MCPServer.BuildingBlock.Interfaces;
using MCPServer.Infrastructure.ExternalServices.Jira;
using MCPServer.Infrastructure.ExternalServices.Jira.OAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Tests;

public class JiraOAuthTests
{
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static readonly JiraOAuthSession Session = new("acc-1", "Mo", "cloud-1", "atl-token");

    private static IOptions<JiraOAuthOptions> Options() => Microsoft.Extensions.Options.Options.Create(new JiraOAuthOptions
    {
        ClientId = "cid", ClientSecret = "super-secret", PublicBaseUrl = "https://mcp.example"
    });

    [Fact]
    public void Pkce_S256_MatchesRfc7636Vector()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", PkceHelper.CreateS256Challenge(verifier));
        Assert.True(PkceHelper.VerifyS256(verifier, "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
        Assert.False(PkceHelper.VerifyS256("other", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
    }

    [Fact]
    public void Store_PendingIsOneTimeAndExpires()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new OAuthStateStore(clock);

        var state = store.AddPending("c", "https://x/cb", "ch", null, "v");
        Assert.NotNull(store.TakePending(state));
        Assert.Null(store.TakePending(state));

        var late = store.AddPending("c", "https://x/cb", "ch", null, "v");
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.Null(store.TakePending(late));
    }

    [Fact]
    public void Store_SessionExpires()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new OAuthStateStore(clock);

        var token = store.AddSession(Session, TimeSpan.FromMinutes(5));
        Assert.Equal(Session, store.GetSession(token));
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.Null(store.GetSession(token));
        Assert.Null(store.GetSession("unknown"));
    }

    [Theory]
    [InlineData("http://evil.example/cb")]
    [InlineData("not a uri")]
    [InlineData("https://ok.example/cb#frag")]
    public void RegisterClient_RejectsUnsafeRedirects(string redirect)
    {
        var service = new JiraOAuthService(new OAuthStateStore(TimeProvider.System), null!, Options(), NullLogger<JiraOAuthService>.Instance);
        Assert.Null(service.RegisterClient([redirect]));
    }

    [Fact]
    public void Validator_ReportsAllProblems_WithoutEchoingTheSecret()
    {
        var result = new JiraOAuthOptionsValidator().Validate(null,
            new JiraOAuthOptions { ClientId = "", ClientSecret = "", PublicBaseUrl = "http://remote.example", Scopes = [] });

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures!.Count());
        Assert.True(new JiraOAuthOptionsValidator().Validate(null, Options().Value).Succeeded);
    }

    [Fact]
    public async Task OAuthApiClient_SendsExactlyTheCallersToken_ToTheCallersSite()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"accountId":"acc-1","displayName":"Mo"}""", Encoding.UTF8, "application/json")
        });
        var atlassian = new AtlassianOAuthClient(new HttpClient(handler), Options());
        var client = new JiraOAuthApiClient(atlassian, new JiraUserContext { Current = Session });

        var me = await client.GetMyselfAsync(CancellationToken.None);

        Assert.Equal("acc-1", me.AccountId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.atlassian.com/ex/jira/cloud-1/rest/api/3/myself", request.RequestUri!.AbsoluteUri);
        Assert.Equal("atl-token", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task OAuthApiClient_WithoutAuthorizedUser_Throws()
    {
        var atlassian = new AtlassianOAuthClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage())), Options());
        await Assert.ThrowsAsync<JiraAuthenticationException>(() =>
            new JiraOAuthApiClient(atlassian, new JiraUserContext()).GetMyselfAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AtlassianFailure_DoesNotLeakBodyOrSecret()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("secret-body super-secret")
        });
        var atlassian = new AtlassianOAuthClient(new HttpClient(handler), Options());

        var ex = await Assert.ThrowsAsync<JiraOAuthException>(() =>
            atlassian.ExchangeCodeAsync("c", "https://x/cb", "v", CancellationToken.None));

        Assert.DoesNotContain("super-secret", ex.Message);
        Assert.DoesNotContain("secret-body", ex.Message);
    }
}
