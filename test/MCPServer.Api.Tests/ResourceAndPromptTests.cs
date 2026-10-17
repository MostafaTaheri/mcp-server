using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace MCPServer.Api.Tests;

public class ResourceAndPromptTests
{
    [Fact]
    public async Task Resources_AreListed_AndReadable()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var templates = await client.ListResourceTemplatesAsync();
        var resources = await client.ListResourcesAsync();
        Assert.Contains(resources, r => r.Uri == "obs://services");
        Assert.Contains(templates, t => t.UriTemplate == "obs://fields/{signal}");

        var services = await client.ReadResourceAsync("obs://services");
        var json = JsonDocument.Parse(((TextResourceContents)services.Contents[0]).Text).RootElement;
        Assert.Equal("checkout", json.GetProperty("services")[0].GetString());
        Assert.False(json.GetProperty("truncated").GetBoolean());

        var fields = await client.ReadResourceAsync("obs://fields/logs");
        var fieldsJson = JsonDocument.Parse(((TextResourceContents)fields.Contents[0]).Text).RootElement;
        Assert.Equal("logs", fieldsJson.GetProperty("signal").GetString());
    }

    [Fact]
    public async Task Prompts_Render_WithArguments_AndAskForEvidence()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var prompts = await client.ListPromptsAsync();
        Assert.Equal(
            ["compare_before_after_deploy", "investigate_error_spike", "why_is_trace_slow"],
            prompts.Select(p => p.Name).Order().ToArray());
        Assert.All(prompts, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));

        var spike = await Render(client, "investigate_error_spike", new() { ["service"] = "checkout", ["timeWindow"] = "1h" });
        Assert.Contains("checkout", spike);
        Assert.Contains("1h", spike);

        var slow = await Render(client, "why_is_trace_slow", new() { ["traceId"] = "abc123" });
        Assert.Contains("abc123", slow);

        var deploy = await Render(client, "compare_before_after_deploy", new() { ["service"] = "cart", ["deployTime"] = "2026-10-16T10:00:00Z" });
        Assert.Contains("2026-10-16T10:00:00Z", deploy);

        foreach (var text in new[] { spike, slow, deploy })
        {
            Assert.Contains("obs_get_connection_status", text);
            Assert.Contains("trace IDs", text);
            Assert.Contains("Kibana links", text);
        }
    }

    private static async Task<string> Render(ModelContextProtocol.Client.McpClient client, string name, Dictionary<string, object?> args)
    {
        var result = await client.GetPromptAsync(name, args);
        return ((TextContentBlock)result.Messages[0].Content).Text;
    }
}
