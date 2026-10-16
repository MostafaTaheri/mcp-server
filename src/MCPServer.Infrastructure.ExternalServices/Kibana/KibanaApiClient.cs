using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Kibana;

public sealed class KibanaApiClient(HttpClient httpClient) : IKibanaApiClient
{
    public async Task<KibanaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/status", cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new KibanaUnauthorizedException();
                case HttpStatusCode.Forbidden:
                    throw new KibanaForbiddenException();
                case not HttpStatusCode.OK:
                    throw new KibanaConnectionException(code: response.StatusCode.ToString());
            }

            var body = await response.Content.ReadFromJsonAsync<StatusDto>(cancellationToken);
            if (string.IsNullOrWhiteSpace(body?.Version?.Number))
            {
                throw new KibanaConnectionException(code: "404", message: "Kibana status response did not contain a version.");
            }

            return new KibanaStatus(body.Version.Number);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       && !cancellationToken.IsCancellationRequested)
        {
            throw new KibanaConnectionException(
                code: "422",
                message: "Could not reach Kibana or read its response. Check Kibana:Url and network connectivity.");
        }
    }

    private sealed record StatusDto([property: JsonPropertyName("version")] VersionDto? Version);

    private sealed record VersionDto([property: JsonPropertyName("number")] string? Number);
}
