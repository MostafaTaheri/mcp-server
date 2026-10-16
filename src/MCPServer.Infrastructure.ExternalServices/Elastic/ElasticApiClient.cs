using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

public sealed class ElasticApiClient(HttpClient httpClient) : IElasticApiClient
{
    public async Task<ElasticClusterInfo> GetClusterInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("", cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new ElasticUnauthorizedException();
                case HttpStatusCode.Forbidden:
                    throw new ElasticForbiddenException();
                case not HttpStatusCode.OK:
                    throw new ElasticAuthenticationException(code: response.StatusCode.ToString());
            }

            var body = await response.Content.ReadFromJsonAsync<InfoDto>(cancellationToken);
            if (string.IsNullOrWhiteSpace(body?.ClusterName) || string.IsNullOrWhiteSpace(body.Version?.Number))
            {
                throw new ElasticAuthenticationException(code: "404", message: "Elasticsearch info response did not contain a cluster name and version.");
            }

            return new ElasticClusterInfo(body.ClusterName, body.Version.Number);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       && !cancellationToken.IsCancellationRequested)
        {
            throw new ElasticAuthenticationException(
                code: "422",
                message: "Could not reach Elasticsearch or read its response. Check Elastic:Url / Elastic:CloudId and network connectivity.");
        }
    }

    public async Task<IReadOnlyList<string>> ListServicesAsync(int max, CancellationToken cancellationToken)
    {
        var request = new
        {
            size = 0,
            aggs = new { services = new { terms = new { field = "service.name", size = max } } }
        };
        var root = await SendAsync(
            () => httpClient.PostAsJsonAsync($"{string.Join(',', SignalPatterns.All)}/_search?ignore_unavailable=true&allow_no_indices=true", request, cancellationToken),
            cancellationToken);

        var names = new List<string>();
        if (root.TryGetProperty("aggregations", out var aggs)
            && aggs.TryGetProperty("services", out var services)
            && services.TryGetProperty("buckets", out var buckets))
        {
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.TryGetProperty("key", out var key) && key.GetString() is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }

        return names.Take(max).Order(StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<ElasticField>> GetFieldsAsync(string signal, int max, CancellationToken cancellationToken)
    {
        if (!SignalPatterns.TryGet(signal, out var pattern))
        {
            throw new ArgumentException("Unknown signal.", nameof(signal));
        }

        var root = await SendAsync(
            () => httpClient.GetAsync($"{pattern}/_field_caps?fields=*&ignore_unavailable=true&allow_no_indices=true", cancellationToken),
            cancellationToken);

        var fields = new List<ElasticField>();
        if (root.TryGetProperty("fields", out var map))
        {
            foreach (var field in map.EnumerateObject())
            {
                if (field.Name.StartsWith('_'))
                {
                    continue;
                }

                var types = field.Value.EnumerateObject().Select(t => t.Name).Order(StringComparer.Ordinal).ToList();
                fields.Add(new ElasticField(field.Name, string.Join('|', types)));
            }
        }

        return fields.OrderBy(f => f.Name, StringComparer.Ordinal).Take(max).ToList();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new ElasticUnauthorizedException();
            case HttpStatusCode.Forbidden:
                throw new ElasticForbiddenException();
            case not HttpStatusCode.OK:
                throw new ElasticAuthenticationException(code: response.StatusCode.ToString());
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task<JsonElement> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send();
            return await ReadAsync(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       && !cancellationToken.IsCancellationRequested)
        {
            throw new ElasticAuthenticationException(
                code: "422",
                message: "Could not reach Elasticsearch or read its response. Check Elastic:Url / Elastic:CloudId and network connectivity.");
        }
    }

    private sealed record InfoDto(
        [property: JsonPropertyName("cluster_name")] string? ClusterName,
        [property: JsonPropertyName("version")] VersionDto? Version);

    private sealed record VersionDto([property: JsonPropertyName("number")] string? Number);
}

internal static class SignalPatterns
{
    public static readonly string[] All = ["logs-*", "traces-*", "metrics-*"];

    public static bool TryGet(string signal, out string pattern)
    {
        pattern = signal switch { "logs" => "logs-*", "traces" => "traces-*", "metrics" => "metrics-*", _ => "" };
        return pattern.Length > 0;
    }
}
