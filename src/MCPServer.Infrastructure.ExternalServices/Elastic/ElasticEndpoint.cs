using System.Text;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

public static class ElasticEndpoint
{
    /// <summary>Resolves the Elasticsearch base address from <c>Url</c> or a Cloud ID; null when neither is usable.</summary>
    public static Uri? Resolve(ElasticOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Url))
        {
            return Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ? uri : null;
        }

        return TryDecodeCloudId(options.CloudId, out var cloud) ? cloud : null;
    }

    /// <summary>Cloud ID format: <c>name:base64(host$elasticsearchId$kibanaId)</c>.</summary>
    public static bool TryDecodeCloudId(string cloudId, out Uri? elasticsearch)
    {
        elasticsearch = null;
        var separator = cloudId.LastIndexOf(':');
        if (separator < 0 || separator == cloudId.Length - 1)
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cloudId[(separator + 1)..]));
            var parts = decoded.Split('$');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return false;
            }

            var host = parts[0].Split(':')[0];
            var port = parts[0].Contains(':') ? parts[0][(parts[0].IndexOf(':') + 1)..] : "443";
            return Uri.TryCreate($"https://{parts[1]}.{host}:{port}", UriKind.Absolute, out elasticsearch);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
