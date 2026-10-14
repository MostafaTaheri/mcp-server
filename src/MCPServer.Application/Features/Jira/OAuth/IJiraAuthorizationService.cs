namespace MCPServer.Application.Features.Jira.OAuth;

public sealed record AuthorizeCommand(string ClientId, string RedirectUri, string CodeChallenge, string? State);

public sealed record TokenCommand(string Code, string ClientId, string RedirectUri, string CodeVerifier);

public sealed record ClientRegistrationResult(string ClientId, IReadOnlyList<string> RedirectUris);

public sealed record AuthenticatedCaller(string AccountId);

/// <summary>
/// Entry point for the API layer's OAuth endpoints and bearer authentication. Every method returns
/// <c>null</c> for invalid, expired or replayed input so callers can answer with a generic error.
/// </summary>
public interface IJiraAuthorizationService
{
    ClientRegistrationResult? RegisterClient(IReadOnlyList<string> redirectUris);

    /// <summary>Returns the Atlassian authorization URL to redirect the user to.</summary>
    Uri? BeginAuthorization(AuthorizeCommand command);

    /// <summary>Returns the client redirect URI carrying a one-time code.</summary>
    Task<Uri?> CompleteCallbackAsync(string code, string state, CancellationToken cancellationToken);

    /// <summary>Returns the opaque session token for a valid one-time code and PKCE verifier.</summary>
    string? ExchangeCode(TokenCommand command);

    /// <summary>Validates a session token and makes that caller the current Jira user for this request.</summary>
    AuthenticatedCaller? Authenticate(string sessionToken);
}
