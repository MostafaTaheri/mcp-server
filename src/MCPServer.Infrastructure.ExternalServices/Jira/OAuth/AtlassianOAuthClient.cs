using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MCPServer.Infrastructure.ExternalServices.Jira.OAuth;

public sealed record AtlassianTokens(string AccessToken, int ExpiresInSeconds);

/// <summary>Talks to Atlassian's OAuth endpoints. Failures surface as <see cref="JiraOAuthException"/> with fixed messages.</summary>
public sealed class AtlassianOAuthClient(HttpClient httpClient, IOptions<JiraOAuthOptions> options)
{
    public async Task<AtlassianTokens> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        var o = options.Value;
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        };

        var dto = await SendAsync<TokenDto>(
            () => new HttpRequestMessage(HttpMethod.Post, o.TokenUrl) { Content = new FormUrlEncodedContent(payload) },
            "422", "Could not exchange the authorization code with Atlassian.", ct);

        if (string.IsNullOrWhiteSpace(dto?.AccessToken))
        {
            throw new JiraOAuthException("401", "Atlassian did not return an access token.");
        }

        return new AtlassianTokens(dto.AccessToken, dto.ExpiresIn > 0 ? dto.ExpiresIn : 3600);
    }

    /// <summary>Returns the cloud ID of the first Jira site the user granted access to.</summary>
    public async Task<string> GetCloudIdAsync(string accessToken, CancellationToken ct)
    {
        var baseUrl = options.Value.ApiBaseUrl;
        var sites = await SendAsync<List<ResourceDto>>(
            () => Authorized(HttpMethod.Get, new Uri(new Uri(baseUrl), "oauth/token/accessible-resources"), accessToken),
            "422", "Could not list the Jira sites available to this account.", ct);

        var id = sites?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Id))?.Id;
        return id ?? throw new JiraOAuthException("403", "The account has not granted access to any Jira site.");
    }

    public async Task<JiraIdentity> GetMyselfAsync(string accessToken, string cloudId, CancellationToken ct)
    {
        var baseUrl = options.Value.ApiBaseUrl;
        var me = await SendAsync<JiraIdentity>(
            () => Authorized(HttpMethod.Get, new Uri(new Uri(baseUrl), $"ex/jira/{Uri.EscapeDataString(cloudId)}/rest/api/3/myself"), accessToken),
            "422", "Could not read the Jira identity for this account.", ct);

        return string.IsNullOrWhiteSpace(me?.AccountId)
            ? throw new JiraOAuthException("404", "Jira identity response did not contain an account ID.")
            : me;
    }

    internal static HttpRequestMessage Authorized(HttpMethod method, Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.Accept.Add(new("application/json"));
        return request;
    }

    private async Task<T?> SendAsync<T>(Func<HttpRequestMessage> build, string code, string message, CancellationToken ct)
    {
        try
        {
            using var request = build();
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new JiraOAuthException(((int)response.StatusCode).ToString(), message);
            }

            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       && !ct.IsCancellationRequested)
        {
            throw new JiraOAuthException(code, message);
        }
    }

    private sealed record TokenDto(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record ResourceDto([property: JsonPropertyName("id")] string? Id);
}

public sealed record JiraIdentity(
    [property: JsonPropertyName("accountId")] string? AccountId,
    [property: JsonPropertyName("displayName")] string? DisplayName);
