using System.Net;
using System.Text;
using MCPServer.Infrastructure.ExternalServices.Elastic;

namespace MCPServer.Infrastructure.ExternalServices.Tests;

public class ElasticCatalogClientTests
{
    private sealed class Handler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static ElasticApiClient Client(Handler h) => new(new HttpClient(h) { BaseAddress = new Uri("https://es.example.com:9200/") });

    [Fact]
    public async Task ListServices_ParsesBuckets_AndCapsAtMax()
    {
        var handler = new Handler(HttpStatusCode.OK,
            """{"aggregations":{"services":{"buckets":[{"key":"cart"},{"key":"checkout"},{"key":"auth"}]}}}""");

        var names = await Client(handler).ListServicesAsync(2, CancellationToken.None);

        Assert.Equal(["cart", "checkout"], names);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
    }

    [Fact]
    public async Task GetFields_FlattensTypes_SkipsMetaFields_AndCaps()
    {
        var handler = new Handler(HttpStatusCode.OK,
            """{"fields":{"_id":{"_id":{}},"message":{"text":{}},"@timestamp":{"date":{}},"level":{"keyword":{},"text":{}}}}""");

        var fields = await Client(handler).GetFieldsAsync("logs", 2, CancellationToken.None);

        Assert.Equal(["@timestamp", "level"], fields.Select(f => f.Name));
        Assert.Equal("keyword|text", fields[1].Type);
        Assert.Contains("logs-*", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Forbidden_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ElasticForbiddenException>(() =>
            Client(new Handler(HttpStatusCode.Forbidden, "{}")).ListServicesAsync(10, CancellationToken.None));
    }
}
