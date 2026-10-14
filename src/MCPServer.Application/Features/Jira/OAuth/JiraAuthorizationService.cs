using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Application.Features.Jira.OAuth;

public sealed class JiraAuthorizationService(IJiraOAuthService oauth, IJiraUserContext userContext)
    : IJiraAuthorizationService
{
    public ClientRegistrationResult? RegisterClient(IReadOnlyList<string> redirectUris)
    {
        var registration = oauth.RegisterClient(redirectUris);
        return registration is null ? null : new ClientRegistrationResult(registration.ClientId, registration.RedirectUris);
    }

    public Uri? BeginAuthorization(AuthorizeCommand command) =>
        oauth.BeginAuthorization(new OAuthAuthorizeRequest(
            command.ClientId, command.RedirectUri, command.CodeChallenge, command.State));

    public Task<Uri?> CompleteCallbackAsync(string code, string state, CancellationToken cancellationToken) =>
        oauth.CompleteCallbackAsync(code, state, cancellationToken);

    public string? ExchangeCode(TokenCommand command) =>
        oauth.ExchangeServerCode(command.Code, command.ClientId, command.RedirectUri, command.CodeVerifier);

    public AuthenticatedCaller? Authenticate(string sessionToken)
    {
        var session = oauth.ValidateSessionToken(sessionToken);
        if (session is null)
        {
            return null;
        }

        userContext.Current = session;
        return new AuthenticatedCaller(session.AccountId);
    }
}
