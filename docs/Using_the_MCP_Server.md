# Using the Observability MCP Server — .NET, Claude, ChatGPT and Kibana

How to connect clients to this server once it is running (locally or deployed). For building and configuring the server itself see the [ReadMe](../ReadMe.md).

## 1. What clients connect to

| | |
|---|---|
| Endpoint | `/mcp` (local: `http://localhost:5187/mcp`, deployed: `https://<your-host>/mcp`) |
| Transport | Streamable HTTP, **stateless** (no `Mcp-Session-Id`). No stdio, no legacy SSE. |
| Client auth | **None today** (per-caller auth is planned as OBS-AUTH-06) |
| Tool | `obs_get_connection_status` — Elastic/Kibana connectivity and versions |
| Resources | `obs://services`, `obs://fields/{signal}` (`logs` \| `traces` \| `metrics`) |
| Prompts | `investigate_error_spike`, `why_is_trace_slow`, `compare_before_after_deploy` |
| Health | `GET /health/live`, `GET /health/ready` |

### Server prerequisites

- `/mcp` is only mapped when `ASPNETCORE_ENVIRONMENT=Development` **or** `Mcp__AllowUnauthenticated=true`. Otherwise it returns 404.
- The server exits at startup if Elastic is not configured (`Elastic__Url` or `Elastic__CloudId`, plus `Elastic__ApiKey` or username/password).
- Because there is no client auth, never expose `/mcp` directly to the internet. Put a gateway, VPN or IP allow-list in front (see [section 7](#7-exposing-the-server-safely)).

```bash
export Elastic__Url=https://my-es:9200 Elastic__ApiKey=... Mcp__AllowUnauthenticated=true
dotnet run --project src/MCPServer.Api
curl -s http://localhost:5187/health/ready
```

## 2. From another .NET application

Works the same whether the server runs on your machine or is already deployed; only the URL changes.

```bash
dotnet add package ModelContextProtocol
```

### Minimal client

```csharp
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("https://mcp.example.com/mcp"),
    // Only if a gateway in front of the server requires it:
    AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = apiKey },
});

await using var client = await McpClient.CreateAsync(transport);

// Tool
var status = await client.CallToolAsync("obs_get_connection_status");
if (status.IsError == true) { /* see "Errors" below */ }
var cluster = status.StructuredContent!.Value.GetProperty("clusterName").GetString();

// Resource (JSON text)
var services = await client.ReadResourceAsync("obs://services");
var json = ((TextResourceContents)services.Contents[0]).Text;

var fields = await client.ReadResourceAsync("obs://fields/logs");

// Prompt
var prompt = await client.GetPromptAsync("investigate_error_spike",
    new Dictionary<string, object?> { ["service"] = "checkout", ["timeWindow"] = "15m" });
var text = ((TextContentBlock)prompt.Messages[0].Content).Text;
```

### Config-driven setup for a service (DI)

`appsettings.json` in the consuming app:

```json
{ "ObservabilityMcp": { "Url": "https://mcp.example.com/mcp", "ApiKey": "" } }
```

```csharp
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>().GetSection("ObservabilityMcp");
    return new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(cfg["Url"]!),
        AdditionalHeaders = string.IsNullOrEmpty(cfg["ApiKey"])
            ? null : new Dictionary<string, string> { ["X-Api-Key"] = cfg["ApiKey"]! },
    });
});
// Connect lazily and share one client (the server is stateless, so reconnecting is cheap).
builder.Services.AddSingleton<Lazy<Task<McpClient>>>(sp =>
    new(() => McpClient.CreateAsync(sp.GetRequiredService<HttpClientTransport>())));
```

Tips:

- Check `GET /health/ready` before connecting in start-up code or a readiness probe.
- Dispose the client (`await using`) when done; the server keeps no session, so there is nothing to clean up server-side.
- The same code works in a console app, an ASP.NET Core service or a `BackgroundService`.
- To let an LLM call the tools, `await client.ListToolsAsync()` returns `McpClientTool` items, which are `AIFunction`s and can be passed to a `Microsoft.Extensions.AI` `IChatClient` through `ChatOptions.Tools`.

### Errors

Tool failures come back with `IsError == true`, text `"{code}: {message} Next: {nextStep}"` and structured content `{ code, message, nextStep }`.

| Code | Meaning |
|---|---|
| `invalid_argument` | Bad input, e.g. unknown `signal` |
| `not_found` | Entity doesn't exist |
| `forbidden` / `auth_required` | Upstream rejected the server's credentials |
| `rate_limited` / `timeout` | Retry later / narrow the query |
| `upstream_unavailable` | Elastic or Kibana unreachable |
| `cursor_expired` | Restart pagination |

## 3. Claude

### Claude Code (works with localhost)

```bash
claude mcp add --transport http observability http://localhost:5187/mcp
claude mcp list          # or /mcp inside a session
```

Prompts appear as slash commands, e.g. `/mcp__observability__investigate_error_spike`. Resources can be referenced with `@observability:obs://services`.

### Claude Desktop and claude.ai

Both use **custom connectors**, which connect from Anthropic's cloud and need a public HTTPS URL (see [section 7](#7-exposing-the-server-safely)); `localhost` will not work.

1. Open **Settings → Connectors** (or **+ → Add custom connector**).
2. Choose **Add custom connector**, enter `https://<your-host>/mcp`, then **Add**.
3. In a chat, enable the connector from the **+** menu.
4. Team/Enterprise: an Owner adds the connector for the organisation first, then members enable it.

Sources: [Claude custom connectors](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp). Menu names change between releases.

Try: *"Check the observability connection, list services, then investigate the error spike in `checkout`."*

## 4. ChatGPT

ChatGPT supports remote MCP servers through **Developer Mode** custom connectors. Plan availability and admin requirements vary by plan and change over time; check OpenAI's current documentation.

1. Enable **Developer Mode** (Settings → Connectors → Advanced).
2. Create a connector, set the server URL to `https://<your-host>/mcp`, and set authentication to **No authentication** (this server has none).
3. ChatGPT connects from the internet, so the URL must be public HTTPS; use a tunnel for local testing.
4. Enable the connector in a chat.

Caveats:

- Some ChatGPT modes (deep research, company knowledge) expect `search` and `fetch` tools. This server does not expose them, so use it in normal chat with Developer Mode.
- Unauthenticated + public = anyone with the URL can read your observability metadata. Protect it first (section 7).

## 5. Kibana

Kibana (9.3+, Preview) can call **external** MCP servers from Agent Builder. This is separate from Kibana's own MCP server.

1. **Stack Management → Connectors → Create connector → MCP**.
2. Enter a name and the server URL `https://<your-host>/mcp`. Add HTTP headers (as secrets) if a gateway in front requires them.
3. Use the connector's **Test** action to check the connection.
4. In **Agent Builder → Tools → New tool**, choose the **MCP** type, pick the connector and import `obs_get_connection_status` (individually or in bulk).
5. Add the tool to an agent and ask it, e.g., *"Is the observability backend connected?"*

The server must be reachable from the Kibana nodes (not from your browser), so a private network address is fine here. Menus and availability are preview and may differ by version; see Elastic's [MCP action type](https://www.elastic.co/docs/reference/kibana/connectors-kibana/mcp-action-type) docs.

## 6. Quick check from any shell

```bash
curl -s http://localhost:5187/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

Or use the MCP Inspector: `npx @modelcontextprotocol/inspector`, transport *Streamable HTTP*, URL `http://localhost:5187/mcp`.

## 7. Exposing the server safely

| Client | Where it connects from | Needs public URL? |
|---|---|---|
| .NET app, Kibana | Your network | No — internal URL is fine |
| Claude Code | Your machine | No |
| Claude Desktop / claude.ai | Anthropic cloud | Yes (HTTPS) |
| ChatGPT | OpenAI cloud | Yes (HTTPS) |

- **Local testing:** a tunnel (`ngrok http 5187`, `cloudflared tunnel --url http://localhost:5187`). Treat the URL as a secret and stop the tunnel afterwards.
- **Deployed:** terminate TLS at a reverse proxy/gateway, set `Mcp__AllowUnauthenticated=true` on the server, and restrict access (VPN, IP allow-list, or gateway-enforced key/OAuth). Cloud clients that cannot send your custom header need an allow-list or OAuth at the gateway.
- Keep Elastic/Kibana credentials only in the server's environment. They are never sent to clients.

## 8. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| 404 on `/mcp` | Not Development and `Mcp__AllowUnauthenticated` not `true` |
| Server exits at start | Missing or invalid `Elastic__*` settings |
| Connector can't connect (Claude/ChatGPT) | URL is `localhost`/private, or not HTTPS |
| 406 / "Not Acceptable" with curl | Add `Accept: application/json, text/event-stream` |
| `isError: true` with `upstream_unavailable` | Server can't reach Elastic/Kibana; check `/health/ready` |
| Stdio config doesn't work | There is no stdio mode; use an HTTP URL |
