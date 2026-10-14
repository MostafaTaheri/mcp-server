using System.Security.Claims;
using System.Text.Encodings.Web;
using MCPServer.Application.Features.Jira.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MCPServer.Api.Auth;

/// <summary>
/// Authenticates MCP calls with the opaque session token issued by the OAuth flow and exposes the caller's
/// Jira session through the Application layer. The 401 challenge is issued by the SDK's MCP scheme.
/// </summary>
public sealed class SessionBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IJiraAuthorizationService authorization) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var caller = authorization.Authenticate(header["Bearer ".Length..].Trim());
        if (caller is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired session."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, caller.AccountId)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
