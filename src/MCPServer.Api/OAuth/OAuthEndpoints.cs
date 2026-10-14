using MCPServer.Application.Features.Jira.OAuth;
using Microsoft.AspNetCore.Mvc;

namespace MCPServer.Api.OAuth;

/// <summary>Metadata, dynamic registration, authorize, callback and token endpoints of the OAuth facade.</summary>
public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapJiraOAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) =>
        {
            var b = BaseUrl(request);
            return Results.Json(new
            {
                issuer = b,
                authorization_endpoint = $"{b}/oauth/authorize",
                token_endpoint = $"{b}/oauth/token",
                registration_endpoint = $"{b}/oauth/register",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" }
            });
        });

        app.MapPost("/oauth/register", ([FromBody] RegistrationRequest body, IJiraAuthorizationService oauth) =>
        {
            var registration = oauth.RegisterClient(body.RedirectUris ?? []);
            return registration is null
                ? Results.Json(new { error = "invalid_redirect_uri" }, statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(new
                {
                    client_id = registration.ClientId,
                    redirect_uris = registration.RedirectUris,
                    token_endpoint_auth_method = "none",
                    grant_types = new[] { "authorization_code" },
                    response_types = new[] { "code" }
                }, statusCode: StatusCodes.Status201Created);
        });

        app.MapGet("/oauth/authorize", (
            [FromQuery(Name = "client_id")] string? clientId,
            [FromQuery(Name = "redirect_uri")] string? redirectUri,
            [FromQuery(Name = "code_challenge")] string? codeChallenge,
            [FromQuery(Name = "code_challenge_method")] string? method,
            [FromQuery(Name = "response_type")] string? responseType,
            string? state,
            IJiraAuthorizationService oauth) =>
        {
            if (clientId is null || redirectUri is null || codeChallenge is null || method != "S256" || responseType != "code")
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            var target = oauth.BeginAuthorization(new AuthorizeCommand(clientId, redirectUri, codeChallenge, state));
            return target is null ? Results.BadRequest(new { error = "invalid_request" }) : Results.Redirect(target.AbsoluteUri);
        });

        app.MapGet("/oauth/callback", async (string? code, string? state, IJiraAuthorizationService oauth, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }

            var redirect = await oauth.CompleteCallbackAsync(code, state, ct);
            return redirect is null ? Results.BadRequest(new { error = "invalid_request" }) : Results.Redirect(redirect.AbsoluteUri);
        });

        app.MapPost("/oauth/token", ([FromForm(Name = "grant_type")] string? grantType,
            [FromForm(Name = "code")] string? code,
            [FromForm(Name = "client_id")] string? clientId,
            [FromForm(Name = "redirect_uri")] string? redirectUri,
            [FromForm(Name = "code_verifier")] string? codeVerifier,
            IJiraAuthorizationService oauth) =>
        {
            var token = grantType == "authorization_code" && code is not null && clientId is not null
                        && redirectUri is not null && codeVerifier is not null
                ? oauth.ExchangeCode(new TokenCommand(code, clientId, redirectUri, codeVerifier))
                : null;

            return token is null
                ? Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(new { access_token = token, token_type = "Bearer", expires_in = 3300 });
        }).DisableAntiforgery();

        return app;
    }

    private static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    public sealed record RegistrationRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("redirect_uris")] string[]? RedirectUris);
}
