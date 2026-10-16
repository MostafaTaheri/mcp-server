using System.Text.Json;
using MCPServer.Application.Mcp;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Api.Tests;

public class ToolErrorTests
{
    private sealed class Upstream(UpstreamFailureKind kind, string message) : Exception(message), IUpstreamFailure
    {
        public UpstreamFailureKind Kind => kind;
    }

    [Theory]
    [InlineData(UpstreamFailureKind.Unauthorized, "auth_required")]
    [InlineData(UpstreamFailureKind.Forbidden, "forbidden")]
    [InlineData(UpstreamFailureKind.NotFound, "not_found")]
    [InlineData(UpstreamFailureKind.RateLimited, "rate_limited")]
    [InlineData(UpstreamFailureKind.Timeout, "timeout")]
    [InlineData(UpstreamFailureKind.Unavailable, "upstream_unavailable")]
    public void Mapper_ClassifiesUpstreamFailures(UpstreamFailureKind kind, string code)
        => Assert.Equal(code, ToolErrorMapper.Map(new Upstream(kind, "x")).Code);

    [Fact]
    public void Mapper_CoversAllDocumentedCodes_AndUnknownIsGeneric()
    {
        var codes = Enum.GetValues<ToolErrorCode>().Select(ToolError.ToCode).ToHashSet();
        Assert.Equal(
            new[] { "invalid_argument", "not_found", "forbidden", "auth_required", "rate_limited", "upstream_unavailable", "timeout", "cursor_expired" }.Order(),
            codes.Order());

        Assert.Equal("upstream_unavailable", ToolErrorMapper.Map(new InvalidOperationException("boom")).Code);
        Assert.Equal("cursor_expired", ToolErrorMapper.Map(new ToolException(ToolErrorCode.CursorExpired, "m", "n")).Code);
    }

    [Fact]
    public async Task ToolFailure_ReturnsStructuredError_WithoutLeakingInternals()
    {
        const string secret = "ApiKey=SECRET123 query={\"match\":\"password\"} elastic_body=at Elasticsearch.Internal";
        await using var factory = new McpTestFactory();
        factory.Status.Failure = () => new Upstream(UpstreamFailureKind.Unauthorized, secret);
        await using var client = await factory.ConnectAsync();

        var result = await client.CallToolAsync("obs_get_connection_status");

        Assert.True(result.IsError);
        var text = string.Join(" ", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        var structured = result.StructuredContent!.Value;
        Assert.Equal("auth_required", structured.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("nextStep").GetString()));
        foreach (var leak in new[] { "SECRET123", "match", "Elasticsearch.Internal" })
        {
            Assert.DoesNotContain(leak, text);
            Assert.DoesNotContain(leak, structured.GetRawText());
        }
    }

    [Fact]
    public async Task ToolException_PassesItsOwnCode()
    {
        await using var factory = new McpTestFactory();
        factory.Status.Failure = () => new ToolException(ToolErrorCode.InvalidArgument, "Bad range.", "Use ISO-8601.");
        await using var client = await factory.ConnectAsync();

        var result = await client.CallToolAsync("obs_get_connection_status");

        Assert.True(result.IsError);
        Assert.Equal("invalid_argument", result.StructuredContent!.Value.GetProperty("code").GetString());
    }
}
