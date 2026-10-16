namespace MCPServer.Api.Tests;

public class ToolContractTests
{
    [Fact]
    public async Task EveryTool_HasDescription_Schemas_AndAnnotations()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.NotEmpty(tools);
        foreach (var tool in tools.Select(t => t.ProtocolTool))
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name}: missing description");
            Assert.Equal(System.Text.Json.JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.True(tool.OutputSchema is not null, $"{tool.Name}: missing output schema");
            Assert.NotNull(tool.Annotations);
            Assert.True(tool.Annotations!.ReadOnlyHint.HasValue, $"{tool.Name}: missing readOnlyHint");
            Assert.True(tool.Annotations.IdempotentHint.HasValue, $"{tool.Name}: missing idempotentHint");
            Assert.True(tool.Annotations.DestructiveHint.HasValue, $"{tool.Name}: missing destructiveHint");
        }
    }

    [Fact]
    public async Task ConnectionStatus_ReturnsStructuredResult()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var result = await client.CallToolAsync("obs_get_connection_status");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("test-cluster", result.StructuredContent!.Value.GetProperty("clusterName").GetString());
    }
}
