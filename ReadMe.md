# MCP Server — Kibana / OTLP

A [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that gives AI clients (Claude Code, Claude Desktop, IDE agents, custom agents) a **read-only window onto your observability data**: the logs, traces and metrics your applications ship to Elastic through OpenTelemetry (OTLP), plus related Kibana objects.

Ask your assistant "why is checkout failing?" and let it gather evidence from Elastic instead of pasting log snippets by hand.

Built with .NET 10 and Clean Architecture.

## Status

The project is in early development.

| Area | State |
|---|---|
| Elasticsearch authentication (API key, username/password, Cloud ID) and startup verification | Done |
| Kibana connection and verification (optional) | Done |
| Typed MCP tools with generated input/output schemas | Done |
| Structured, actionable tool errors | Done |
| Tool `obs_get_connection_status` | Done |
| Read-only resources `obs://services`, `obs://fields/{signal}` | Done |
| Investigation prompts (error spike, slow trace, before/after deploy) | Done |
| Log, trace and metric search tools, Kibana objects, per-caller auth, redaction | Planned |

See [docs/Kibana_OTLP_MCP_Server_User_Stories.md](docs/Kibana_OTLP_MCP_Server_User_Stories.md) for the full plan and [docs/Implementation_Progress.md](docs/Implementation_Progress.md) for what has shipped. Nothing has been verified against a live Elastic cluster yet.

## Principles

- **Read-only.** The server never writes to Elasticsearch or Kibana.
- **Typed tools.** Every tool has a description, input and output schema, and read-only/idempotent/destructive annotations. A contract test enforces this.
- **Actionable errors.** Failures come back as `isError` results with `{ code, message, nextStep }`. Raw exceptions and upstream text are only logged, never returned.
- **Fail fast.** On startup the server validates its configuration (all problems reported at once) and calls Elasticsearch. It exits with code 1 if the config is invalid or the credentials are rejected.
- **No secrets in output.** Credentials are never logged or returned.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Elasticsearch cluster (Elastic Cloud or self-managed) and an API key, or a username and password
- Optional: a Kibana URL

## Quick start

```bash
export Elastic__Url="https://my-cluster.example.com:9200"   # or Elastic__CloudId="<cloud id>"
export Elastic__ApiKey="<base64 api key>"                    # or Elastic__Username / Elastic__Password

dotnet run --project src/MCPServer.Api
```

The Elasticsearch settings are required. Without them the server exits with code 1.

The server listens on `http://localhost:5187` (and `https://localhost:7082`) in Development. On success the log shows the Elasticsearch cluster name and version.

Health endpoints:

- `GET /health/live`
- `GET /health/ready`

In Development, API docs are served by Scalar (`/scalar`) and OpenAPI (`/openapi/v1.json`).

## Configuration

Settings live under `Elastic` and `Kibana` in [appsettings.json](src/MCPServer.Api/appsettings.json). Keep secrets out of that file and use environment variables (`__` replaces `:`).

### Elasticsearch

Configure exactly one endpoint and exactly one authentication method.

| Setting | Description |
|---|---|
| `Elastic__Url` | Cluster URL. Use this **or** `CloudId`. |
| `Elastic__CloudId` | Elastic Cloud ID, decoded to the Elasticsearch host. |
| `Elastic__ApiKey` | Base64 API key. Use this **or** username + password. |
| `Elastic__Username` / `Elastic__Password` | Basic authentication. |
| `Elastic__TimeoutSeconds` | Request timeout (default 30). |

### Kibana (optional)

```bash
export Kibana__Url="https://my-kibana.example.com:5601"
```

Credentials default to the Elastic ones. Set `Kibana__ApiKey` or `Kibana__Username` / `Kibana__Password` to override. When `Kibana__Url` is set, the server verifies `GET /api/status` and logs the version. When it is not set, Kibana is not registered and Elasticsearch works as before.

### MCP endpoint

| Setting | Description |
|---|---|
| `Mcp__AllowUnauthenticated` | Default `false`. `/mcp` is always mapped in Development. In Production it is mapped only when this is `true`, and the server logs a warning. |

### Telemetry

The server's own logs and traces go to an OTLP endpoint (`OpenTelemetry` and `Serilog` sections of `appsettings.json`, default `http://localhost:4317`). Override with `OpenTelemetry__Endpoint`. The `service.name` is still the template value `boiler-plate`.

## Connecting an MCP client

The server speaks MCP over **Streamable HTTP** (stateless) at `/mcp`. For .NET, Claude, ChatGPT and Kibana setup see [docs/Using_the_MCP_Server.md](docs/Using_the_MCP_Server.md).

**Claude Code**

```bash
claude mcp add --transport http observability http://localhost:5187/mcp
```

**Other clients** (generic JSON config)

```json
{
  "mcpServers": {
    "observability": {
      "type": "http",
      "url": "http://localhost:5187/mcp"
    }
  }
}
```

Then try a prompt such as: *"Check that the observability server can reach Elasticsearch."*

## Tools

| Tool | Description |
|---|---|
| `obs_get_connection_status` | Checks that the server can reach Elasticsearch (and Kibana, if configured) and returns the cluster name and versions. Takes no arguments. Read-only and idempotent. |

## Resources

Read-only context an assistant can load without a tool call. Content is bounded and cached for 5 minutes.

| URI | Content |
|---|---|
| `obs://services` | Service names (`service.name`) seen in logs, traces and metrics. At most 200, sorted. |
| `obs://fields/{signal}` | Field names and types for `logs`, `traces` or `metrics`. At most 1000, sorted. |

Both return JSON with `count` and a `truncated` flag. An unknown signal returns `invalid_argument`. A mapping-profile resource is planned with the OTel mapping profiles.

## Prompts

Ready-made investigation procedures. Each tells the assistant to check connectivity, read the resources above, work from narrow to wide, and cite evidence (trace IDs, log IDs, Kibana links).

| Prompt | Arguments |
|---|---|
| `investigate_error_spike` | `service`, `timeWindow` (default `15m`) |
| `why_is_trace_slow` | `traceId` |
| `compare_before_after_deploy` | `service`, `deployTime`, `timeWindow` (default `30m`) |

### Error format

A failing tool call returns `isError: true` with:

```json
{ "code": "upstream_unavailable", "message": "...", "nextStep": "..." }
```

Codes: `invalid_argument`, `not_found`, `forbidden`, `auth_required`, `rate_limited`, `upstream_unavailable`, `timeout`, `cursor_expired`.

## Project structure

```
src/
  MCPServer.Api                            MCP endpoint, tools, resources, prompts, error filter, middleware, health checks
  MCPServer.Application                    Use cases (connection status, catalogs) and tool error mapping
  MCPServer.BuildingBlock                  Shared interfaces and contracts
  MCPServer.Composition                    Dependency-injection wiring
  MCPServer.Domain                         Domain model
  MCPServer.Infrastructure.DataAccess      Data access
  MCPServer.Infrastructure.ExternalServices  Elasticsearch and Kibana clients, startup verifiers
test/
  MCPServer.Api.Tests                      MCP tool and contract tests through the SDK client
  MCPServer.ArchTests                      Architecture rules (layer dependencies)
  MCPServer.Infrastructure.ExternalServices.Tests  Options, clients and verifier tests
```

The Api layer calls only the Application layer. Architecture tests enforce the layer boundaries.

## Testing

```bash
dotnet test MCPServer.slnx
```

## Security notes

- `/mcp` has no per-caller authentication yet (planned as OBS-AUTH-06). It is mapped in Development, and in Production only with `Mcp__AllowUnauthenticated=true`. If you enable that, put the server behind a gateway or VPN that authenticates callers.
- Use a least-privilege, read-only Elastic API key.
- Provide credentials through environment variables or a secret store, never commit them.
