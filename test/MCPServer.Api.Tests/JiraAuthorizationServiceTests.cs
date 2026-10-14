using MCPServer.Application.Features.Jira.OAuth;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Api.Tests;

public class JiraAuthorizationServiceTests
{
    private sealed class FakeOAuth(JiraOAuthSession? session) : IJiraOAuthService
    {
        public OAuthClientRegistration? RegisterClient(IReadOnlyList<string> redirectUris) => null;
        public Uri? BeginAuthorization(OAuthAuthorizeRequest request) => null;
        public Task<Uri?> CompleteCallbackAsync(string code, string state, CancellationToken cancellationToken) => Task.FromResult<Uri?>(null);
        public string? ExchangeServerCode(string code, string clientId, string redirectUri, string codeVerifier) => null;
        public JiraOAuthSession? ValidateSessionToken(string sessionToken) => session;
    }

    private sealed class Context : IJiraUserContext
    {
        public JiraOAuthSession? Current { get; set; }
    }

    [Fact]
    public void Authenticate_ValidToken_SetsCurrentUser()
    {
        var session = new JiraOAuthSession("acc-1", "Ada", "cloud-1", "atl-token");
        var context = new Context();

        var caller = new JiraAuthorizationService(new FakeOAuth(session), context).Authenticate("t");

        Assert.Equal("acc-1", caller?.AccountId);
        Assert.Same(session, context.Current);
    }

    [Fact]
    public void Authenticate_InvalidToken_ReturnsNullAndLeavesContextEmpty()
    {
        var context = new Context();

        var caller = new JiraAuthorizationService(new FakeOAuth(null), context).Authenticate("bad");

        Assert.Null(caller);
        Assert.Null(context.Current);
    }
}
