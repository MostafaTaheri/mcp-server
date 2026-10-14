namespace MCPServer.BuildingBlock.Interfaces;

/// <summary>The Jira identity and Atlassian access token behind one authorized MCP session.</summary>
public sealed record JiraOAuthSession(string AccountId, string? DisplayName, string CloudId, string AccessToken);

/// <summary>A client registered through dynamic client registration.</summary>
public sealed record OAuthClientRegistration(string ClientId, IReadOnlyList<string> RedirectUris);

/// <summary>The MCP client's authorization request (always PKCE S256).</summary>
public sealed record OAuthAuthorizeRequest(string ClientId, string RedirectUri, string CodeChallenge, string? State);

/// <summary>
/// Atlassian OAuth 2.0 (3LO) facade. Methods return <c>null</c> for any invalid, expired or replayed input,
/// so callers can answer with a generic error without leaking details.
/// </summary>
public interface IJiraOAuthService
{
    /// <summary>Registers a client; returns <c>null</c> when a redirect URI is not https or loopback.</summary>
    OAuthClientRegistration? RegisterClient(IReadOnlyList<string> redirectUris);

    /// <summary>Validates the client request and returns the Atlassian authorization URL to redirect to.</summary>
    Uri? BeginAuthorization(OAuthAuthorizeRequest request);

    /// <summary>Handles the Atlassian callback; returns the client redirect URI carrying a one-time code.</summary>
    Task<Uri?> CompleteCallbackAsync(string code, string state, CancellationToken cancellationToken);

    /// <summary>Exchanges the one-time code (verified with the client's PKCE verifier) for a session token.</summary>
    string? ExchangeServerCode(string code, string clientId, string redirectUri, string codeVerifier);

    /// <summary>Resolves a bearer session token; <c>null</c> when unknown or expired.</summary>
    JiraOAuthSession? ValidateSessionToken(string sessionToken);
}

/// <summary>Per-request holder of the authorized caller; set by authentication, read by the Jira client.</summary>
public interface IJiraUserContext
{
    JiraOAuthSession? Current { get; set; }
}
