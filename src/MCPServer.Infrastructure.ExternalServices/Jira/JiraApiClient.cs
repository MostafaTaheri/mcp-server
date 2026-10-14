using MCPServer.BuildingBlock.Interfaces;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MCPServer.Infrastructure.ExternalServices.Jira;

public sealed class JiraApiClient(HttpClient httpClient) : IJiraApiClient
{
    public async Task<JiraMyself> GetMyselfAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("rest/api/3/myself", cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new JiraUnauthorizedException();
                case HttpStatusCode.Forbidden:
                    throw new JiraForbiddenException();
                case not HttpStatusCode.OK:
                    throw new JiraAuthenticationException(code: response.StatusCode.ToString());
            }

            var body = await response.Content.ReadFromJsonAsync<MyselfDto>(cancellationToken);
            if (string.IsNullOrWhiteSpace(body?.AccountId))
            {
                throw new JiraAuthenticationException(code: "404", message: "Jira identity response did not contain an account ID.");
            }

            return new JiraMyself(body.AccountId, body.DisplayName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       && !cancellationToken.IsCancellationRequested)
        {
            throw new JiraAuthenticationException(
                code: "422",
                message: "Could not reach the Jira site or read its response. Check Jira:SiteUrl and network connectivity.");
        }
    }

    private sealed record MyselfDto(
        [property: JsonPropertyName("accountId")] string? AccountId,
        [property: JsonPropertyName("displayName")] string? DisplayName);
}
