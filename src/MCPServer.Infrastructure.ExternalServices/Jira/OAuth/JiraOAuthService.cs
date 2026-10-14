using System.Web;
using MCPServer.BuildingBlock.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

public sealed class JiraOAuthService(
    OAuthStateStore store,
    AtlassianOAuthClient atlassian,
    IOptions<JiraOAuthOptions> options,
    ILogger<JiraOAuthService> logger) : IJiraOAuthService
{
    public OAuthClientRegistration? RegisterClient(IReadOnlyList<string> redirectUris)
    {
        if (redirectUris.Count == 0 || !redirectUris.All(IsAllowedRedirect))
        {
            return null;
        }

        return new OAuthClientRegistration(store.AddClient(redirectUris), redirectUris);
    }

    public Uri? BeginAuthorization(OAuthAuthorizeRequest request)
    {
        var registered = store.GetClientRedirects(request.ClientId);
        if (registered is null || !registered.Contains(request.RedirectUri, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(request.CodeChallenge))
        {
            return null;
        }

        var o = options.Value;
        var verifier = PkceHelper.CreateVerifier();
        var state = store.AddPending(request.ClientId, request.RedirectUri, request.CodeChallenge, request.State, verifier);

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["audience"] = "api.atlassian.com";
        query["client_id"] = o.ClientId;
        query["scope"] = string.Join(' ', o.Scopes);
        query["redirect_uri"] = CallbackUri(o);
        query["state"] = state;
        query["response_type"] = "code";
        query["prompt"] = "consent";
        query["code_challenge"] = PkceHelper.CreateS256Challenge(verifier);
        query["code_challenge_method"] = "S256";
        return new Uri($"{o.AuthorizeUrl}?{query}");
    }

    public async Task<Uri?> CompleteCallbackAsync(string code, string state, CancellationToken cancellationToken)
    {
        var pending = store.TakePending(state);
        if (pending is null)
        {
            logger.LogWarning("OAuth callback rejected: unknown or expired state");
            return null;
        }

        var o = options.Value;
        try
        {
            var tokens = await atlassian.ExchangeCodeAsync(code, CallbackUri(o), pending.ServerVerifier, cancellationToken);
            var cloudId = await atlassian.GetCloudIdAsync(tokens.AccessToken, cancellationToken);
            var me = await atlassian.GetMyselfAsync(tokens.AccessToken, cloudId, cancellationToken);

            var lifetime = TimeSpan.FromSeconds(Math.Min(tokens.ExpiresInSeconds, o.SessionLifetimeMinutes * 60));
            var session = new JiraOAuthSession(me.AccountId!, me.DisplayName, cloudId, tokens.AccessToken);
            var serverCode = store.AddCode(pending, session, lifetime);

            logger.LogInformation("Jira OAuth completed for account {AccountId}", session.AccountId);
            return Redirect(pending.RedirectUri, ("code", serverCode), ("state", pending.ClientState));
        }
        catch (JiraOAuthException ex)
        {
            logger.LogWarning("Jira OAuth callback failed: {Code} {Message}", ex.Code, ex.Message);
            return Redirect(pending.RedirectUri, ("error", "access_denied"), ("state", pending.ClientState));
        }
    }

    public string? ExchangeServerCode(string code, string clientId, string redirectUri, string codeVerifier)
    {
        var issued = store.TakeCode(code);
        if (issued is null
            || !string.Equals(issued.ClientId, clientId, StringComparison.Ordinal)
            || !string.Equals(issued.RedirectUri, redirectUri, StringComparison.Ordinal)
            || !PkceHelper.VerifyS256(codeVerifier, issued.ClientChallenge))
        {
            return null;
        }

        return store.AddSession(issued.Session, issued.SessionLifetime);
    }

    public JiraOAuthSession? ValidateSessionToken(string sessionToken) => store.GetSession(sessionToken);

    private static string CallbackUri(JiraOAuthOptions o) => o.PublicBaseUrl.TrimEnd('/') + o.CallbackPath;

    private static bool IsAllowedRedirect(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    private static Uri Redirect(string redirectUri, params (string Key, string? Value)[] parameters)
    {
        var builder = new UriBuilder(redirectUri);
        var query = HttpUtility.ParseQueryString(builder.Query);
        foreach (var (key, value) in parameters)
        {
            if (value is not null)
            {
                query[key] = value;
            }
        }

        builder.Query = query.ToString();
        return builder.Uri;
    }
}
