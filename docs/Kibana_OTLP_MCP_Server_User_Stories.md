# Kibana / OTLP MCP Server — User Stories

**Prepared for:** Mostafa Taheri — Technical Lead | Software Architect
**Status:** Draft v0.1 for review
**Solution:** `MCPServer` (.NET 10, Clean Architecture)
**Related:** [Jira_MCP_Server_User_Stories.md](Jira_MCP_Server_User_Stories.md) (same conventions)

## 1. Purpose and scope

Build a production-grade [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that lets AI clients (Claude Desktop, Claude Code, IDE agents, custom agents) **investigate production behaviour** by querying the logs, traces and metrics that applications ship to Elastic through OpenTelemetry (OTLP), plus the related Kibana objects. The server is a **read-only** window onto observability data: it never writes to Elasticsearch or Kibana.

This is the second integration on the shared MCP host (after Jira). It reuses the host, authentication pipeline, resilience and telemetry building blocks, and must not rework the core.

**In scope:**
- Elasticsearch search over OTLP data (logs, traces/spans, metrics) on **Elastic Cloud and self-managed** clusters.
- Kibana read APIs: spaces, data views, saved searches, dashboards, deep links.
- MCP tools, resources and prompts; authentication; query safety and data protection; resilience; self-observability; packaging and operations.

**Out of scope for this phase:**
- Ingesting or forwarding OTLP (no OTLP receiver, no writes to Elasticsearch).
- Creating or changing Kibana objects, alert rules, cases, ML jobs, or Fleet/agent policies.
- Elastic Security (SIEM) detections, Elastic RUM / session replay, profiling.
- Cross-cluster search, a UI, and push notifications (see Q3, Q6).

## 2. Personas

| Persona | Description |
|---|---|
| **AI Agent / MCP Client** | The LLM-driven client that calls tools. Needs predictable schemas, bounded output and actionable errors. |
| **On-call / Developer** | Person who asks their assistant "why is checkout failing?" and expects evidence from logs, traces and metrics. |
| **Elastic Admin** | Owns the Elastic deployment; controls which indices, spaces and credentials the server may use. |
| **Platform / SRE** | Deploys, monitors and operates this server. |
| **Security / Compliance Officer** | Needs least privilege, auditability and guarantees that sensitive log content is protected. |

## 3. Assumptions and open questions

| # | Assumption (default used in these stories) | Question to confirm |
|---|---|---|
| Q1 | Elastic **8.x and 9.x**, Elastic Cloud (Hosted/Serverless) and self-managed. | Is Elastic 7.x or Serverless-only API surface a requirement? |
| Q2 | OTLP data lands in Elastic **data streams** via the Elastic Distribution of OpenTelemetry (EDOT) / APM Server (`traces-*`, `logs-*`, `metrics-*`), using OpenTelemetry semantic-convention fields. Custom index layouts are supported through configuration. | Are there legacy `apm-*` indices or fully custom index names in use? |
| Q3 | One Elasticsearch/Kibana deployment per server instance; **multiple named connections** may be configured. | Is cross-cluster search (CCS) required? |
| Q4 | Auth to Elastic is an **API key** (default), username/password, or Cloud ID + API key; the server never creates credentials. | Is per-user identity pass-through (so Elastic RBAC applies per person) mandatory for remote hosting? |
| Q5 | Both **stdio** (local) and **Streamable HTTP** (remote) transports are supported. | Is remote hosting required for MVP? |
| Q6 | Queries use the **Elasticsearch Query DSL and ES\|QL** internally; callers never send raw queries in MVP. | Should power users be able to run raw ES\|QL (guarded)? |
| Q7 | The server is **stateless**; metadata cache is in-memory. | Is Redis acceptable as an infrastructure dependency? |
| Q8 | Log bodies may contain **PII or secrets**; redaction is on by default. | Which fields/patterns must be redacted for our data classification? |

## 4. Story conventions

- **ID:** `OBS-<EPIC>-<nn>`.
- **Priority (MoSCoW):** M = Must, S = Should, C = Could.
- **Release:** MVP → v1 → Later (see section 8).
- **Acceptance criteria** use Given/When/Then, abbreviated as bullets.
- **Time ranges** are always explicit or defaulted (last 15 minutes) and expressed in UTC (ISO 8601); relative forms (`now-1h`) are accepted.
- **Definition of Done (all stories):** code reviewed; unit and integration tests added (Elasticsearch via Testcontainers or a fake); architecture tests pass; no secrets, queries' sensitive values or log content in the server's own logs; docs/tool catalog updated; build has no warnings (`TreatWarningsAsErrors`).

---

## 5. Epics and stories

### E1 — Connectivity and authentication (`AUTH`)

**OBS-AUTH-01 · API key authentication · M · MVP**
As an *On-call / Developer*, I want to connect with an Elasticsearch API key so that I can start using the server in minutes.
- Given a valid Elasticsearch URL (or Cloud ID) and API key, when the server starts, then it verifies connectivity and logs the cluster name and version (not the key).
- Given an invalid or expired key, when the server starts, then it fails fast with a clear, non-leaking error and a non-zero exit code.

**OBS-AUTH-02 · Username/password and Cloud ID support · S · v1**
As an *Elastic Admin*, I want basic authentication and Cloud ID configuration so that self-managed and Elastic Cloud deployments both work.
- Given username/password or Cloud ID configuration, when the server starts, then it connects using exactly one configured method.
- Given conflicting methods are configured, when the server starts, then validation reports all problems at once.

**OBS-AUTH-03 · Kibana connection and verification · M · MVP**
As an *On-call / Developer*, I want the server to connect to Kibana as well so that results can link back to Kibana.
- Given a Kibana URL and credentials, when the server starts, then it verifies `GET /api/status` and logs the version.
- Given Kibana is not configured, when the server starts, then Kibana tools are not registered and Elasticsearch tools still work.

**OBS-AUTH-04 · TLS and certificate trust · M · MVP**
As a *Security Officer*, I want TLS verification on by default with support for a custom CA so that credentials and telemetry are never sent over an untrusted channel.
- Given an HTTPS endpoint with a private CA, when a CA bundle or fingerprint is configured, then the connection succeeds.
- Given certificate validation fails, when connecting, then the server refuses and never silently disables validation (an explicit, logged-as-warning dev-only flag is the only exception, and is rejected in Production).

**OBS-AUTH-05 · Secure secret handling · M · MVP**
As a *Security Officer*, I want credentials loaded from environment variables or a secret store so that no secret is committed or logged.
- Secrets are never written to logs, traces, error messages or tool results.
- Configuration supports env vars and a pluggable secret provider (e.g. Key Vault, Docker/K8s secrets).
- Rotating a secret requires a restart at most; no secret is cached beyond process memory.

**OBS-AUTH-06 · Per-user identity pass-through for remote hosting · S · v1**
As a *Security Officer*, I want each HTTP caller authenticated and their own Elastic credentials (or delegated token) used so that Elastic RBAC and audit apply per person.
- Given an unauthenticated HTTP client, when it calls a tool, then it receives a `401` challenge with protected-resource metadata (same pattern as the Jira server).
- Given an authenticated caller, when a query runs, then Elasticsearch sees that caller's identity (e.g. `es-secondary-authorization` / user-scoped API key), never a shared super-user.
- Given a caller has no access to an index, when they query it, then they get a `forbidden` result, not another user's data.

### E2 — Log search (`LOG`)

**OBS-LOG-01 · Search logs · M · MVP**
As an *On-call / Developer*, I want to search logs by time range, service, environment, severity and free text so that I can find relevant evidence quickly.
- Given filters for time range, `service.name`, `deployment.environment`, severity and text, when `logs_search` is called, then matching entries are returned newest-first with timestamp, service, severity, body, `trace_id` and `span_id`.
- Given no time range, when called, then the last 15 minutes is used and the effective range is echoed in the result.
- Given no matches, when called, then an empty result with the effective query summary is returned (not an error).

**OBS-LOG-02 · Get log entry by ID · M · MVP**
As an *AI Agent*, I want to fetch a single log entry with all its attributes so that I can read full detail after a search.
- Given an index and document ID from a search result, when `logs_get` is called, then the full record including resource and log attributes is returned (subject to redaction).
- Given the document does not exist or is not accessible, when called, then the same `not_found` error is returned for both cases.

**OBS-LOG-03 · Recent errors summary · M · MVP**
As an *On-call / Developer*, I want a grouped summary of recent errors so that I see the top problems instead of thousands of lines.
- Given a time range and optional service, when `logs_error_summary` is called, then errors are grouped by service and normalized message pattern with count, first seen, last seen and one sample.
- Given more groups than the cap, when called, then the top N by count are returned with `truncated: true`.

**OBS-LOG-04 · Log context around an entry · S · v1**
As an *On-call / Developer*, I want the log lines before and after a given entry so that I can see what led to it.
- Given a log entry and a window (default ±20 entries or ±30 s), when `logs_context` is called, then entries from the same service/host/pod are returned in order.
- Given the window exceeds the cap, when called, then it is clamped and the clamp is reported.

**OBS-LOG-05 · Logs for a trace · M · v1**
As an *On-call / Developer*, I want all logs correlated to a trace ID so that I can see what happened during a request.
- Given a `trace_id`, when `logs_by_trace` is called, then logs across all services carrying that ID are returned in time order.
- Given logs lack `trace_id`, when called, then an empty result explains that correlation fields were not found.

**OBS-LOG-06 · Log volume over time · S · v1**
As an *On-call / Developer*, I want log counts bucketed over time by severity so that I can spot spikes.
- Given a time range and filters, when `logs_histogram` is called, then buckets with counts per severity are returned using an automatic interval within the bucket cap.

**OBS-LOG-07 · Bounded, truncated output · M · MVP**
As an *AI Agent*, I want result size limited and truncation signalled so that responses fit my context window.
- Given a result exceeds the configured size or token budget, when returned, then long fields are truncated with a marker, `truncated: true` is set, and a cursor/narrowing hint is provided.
- Default page size is 50, hard maximum 500.

### E3 — Traces (`TRACE`)

**OBS-TRACE-01 · Get trace by ID · M · MVP**
As an *On-call / Developer*, I want the full span tree of a trace so that I can understand a request end to end.
- Given a `trace_id`, when `traces_get` is called, then spans are returned as a tree with service, name, duration, status, key attributes and error events.
- Given a trace exceeds the span cap, when called, then the slowest and errored spans are kept and `truncated: true` is set.

**OBS-TRACE-02 · Search traces · M · MVP**
As an *On-call / Developer*, I want to find traces by service, operation, duration, status and attributes so that I can locate slow or failing requests.
- Given filters (service, span name, min duration, error only, attribute equals), when `traces_search` is called, then root-span summaries are returned sorted by start time or duration.
- Given an invalid filter, when called, then the error names the invalid parameter.

**OBS-TRACE-03 · Slow-span / latency analysis · S · v1**
As an *On-call / Developer*, I want to know where time is spent in a service so that I can find the bottleneck.
- Given a service and range, when `traces_latency_breakdown` is called, then p50/p95/p99 per operation and the top slowest downstream spans are returned.

**OBS-TRACE-04 · Service dependency map · S · v1**
As an *On-call / Developer*, I want upstream/downstream dependencies of a service with call rate and error rate so that I can see blast radius.
- Given a service and range, when `services_dependencies` is called, then caller/callee pairs with request count, error rate and average latency are returned.

**OBS-TRACE-05 · Error spans and exception details · S · v1**
As an *On-call / Developer*, I want the exception type, message and stack trace attached to failing spans so that I can diagnose the root cause.
- Given an errored span, when returned by `traces_get`, then `exception.type`, `exception.message` and a length-capped `exception.stacktrace` are included (subject to redaction).

### E4 — Metrics (`METRIC`)

**OBS-METRIC-01 · Discover metrics · M · v1**
As an *AI Agent*, I want to list available metrics with type, unit and description so that I can choose valid names instead of guessing.
- Given an optional service or name prefix, when `metrics_list` is called, then metric names, instrument type, unit and the attribute keys seen are returned from the cached metadata.

**OBS-METRIC-02 · Query a metric time series · M · v1**
As an *On-call / Developer*, I want to chart a metric over time with an aggregation and grouping so that I can compare behaviour before and after an event.
- Given a metric, range, aggregation (avg/min/max/sum/rate/percentile) and optional `group_by`, when `metrics_query` is called, then bucketed points are returned within the point cap.
- Given an aggregation invalid for the metric's instrument type, when called, then the error explains the valid options.

**OBS-METRIC-03 · Service health summary (RED) · M · v1**
As an *On-call / Developer*, I want request rate, error rate and latency for a service in one call so that I can assess health at a glance.
- Given a service and range, when `services_health` is called, then rate, error %, p95 latency and the comparison against the previous period are returned.

**OBS-METRIC-04 · Infrastructure metrics for a resource · C · Later**
As an *SRE*, I want CPU, memory and restarts for a host or Kubernetes pod so that I can correlate saturation with errors.
- Given a `host.name` or `k8s.pod.name`, when queried, then standard system metrics are returned in the same format as `metrics_query`.

### E5 — OTLP and resource semantics (`OTEL`)

**OBS-OTEL-01 · Discover services and environments · M · MVP**
As an *AI Agent*, I want to list services, environments and versions that have reported data so that I can scope queries correctly.
- Given a time range, when `services_list` is called, then services with environment, version, language/SDK and last-seen time are returned.

**OBS-OTEL-02 · Semantic-convention field mapping · M · MVP**
As an *Elastic Admin*, I want logical fields (service, severity, trace ID, duration…) mapped to physical field names per index layout so that the same tools work with EDOT, legacy APM and custom indices.
- Given a mapping profile (`edot`, `apm-legacy`, `custom`), when a tool runs, then queries use that profile's field names.
- Given a custom profile in configuration, when the server starts, then it is validated and unknown fields are reported.

**OBS-OTEL-03 · Index and data-stream discovery · S · v1**
As an *On-call / Developer*, I want to see which data streams hold logs, traces and metrics and what fields they expose so that I can understand what data exists.
- Given the allow-listed patterns, when `data_sources_list` is called, then matching data streams, document counts, time coverage and a sample of field names are returned.

**OBS-OTEL-04 · Resource attribute filtering · S · v1**
As an *On-call / Developer*, I want to filter by arbitrary resource attributes (`k8s.namespace.name`, `cloud.region`, `host.name`…) so that I can narrow to a region, node or pod.
- Given an attribute filter list, when any search tool is called, then each filter is applied as an exact match on the mapped attribute field.
- Given an attribute name that is not allow-listed, when called, then it is rejected with the list of allowed names.

### E6 — Kibana objects (`KIB`)

**OBS-KIB-01 · Kibana deep links in results · M · v1**
As an *On-call / Developer*, I want a Kibana link on every log, trace and metric result so that I can open the same view in the browser.
- Given Kibana is configured, when results are returned, then each carries a Discover/APM/Metrics URL with the same time range and filters applied.

**OBS-KIB-02 · List spaces and data views · S · v1**
As an *AI Agent*, I want to list Kibana spaces and data views so that I can query within the right space.
- Given a caller, when `kibana_spaces_list` / `kibana_data_views_list` is called, then only spaces and views the caller can access are returned.

**OBS-KIB-03 · Read saved searches · C · Later**
As an *On-call / Developer*, I want to read my team's saved searches so that I can reuse known-good queries.
- Given a saved-search ID or title, when `kibana_saved_search_get` is called, then its filters and columns are returned in a form usable by `logs_search`.

**OBS-KIB-04 · Read dashboards · C · Later**
As an *On-call / Developer*, I want a dashboard's title and panel descriptions so that my assistant knows what the team already monitors.
- Given a dashboard ID, when `kibana_dashboard_get` is called, then title, description and a panel summary (type, title, data view, query) are returned without executing the panels.

### E7 — Query safety and data governance (`SAFE`)

**OBS-SAFE-01 · Read-only enforcement · M · MVP**
As a *Security Officer*, I want the server physically unable to modify Elastic data so that an AI mistake cannot cause damage.
- Only `_search`, `_msearch`, `_count`, `_field_caps`, `_mapping`, `_resolve/index`, `_query` (ES|QL) and Kibana GET endpoints are callable; an architecture test fails if any other HTTP verb/path is added.
- The documented minimum Elastic role has only `read`, `view_index_metadata` on allow-listed patterns.

**OBS-SAFE-02 · Index allow-list · M · MVP**
As an *Elastic Admin*, I want to restrict which indices/data streams the server may query so that unrelated or sensitive data is unreachable.
- Given allow-listed patterns, when a tool targets anything outside them, then the call is rejected before reaching Elasticsearch.
- Default allow-list covers only OTLP data streams (`logs-*`, `traces-*`, `metrics-*` by profile), never `*`.

**OBS-SAFE-03 · Query cost guardrails · M · MVP**
As a *Platform / SRE*, I want caps on time range, result size, bucket count and query time so that a single tool call cannot overload the cluster.
- Configurable defaults: max range 24 h (hard max 30 d), max size 500, max buckets 200, server-side timeout 15 s.
- Given a request over a cap, when called, then it is clamped or rejected with a clear message stating the limit.
- Wildcard-leading text queries and unbounded aggregations are rejected.

**OBS-SAFE-04 · Sensitive data redaction · M · MVP**
As a *Security Officer*, I want sensitive values masked before results leave the server so that LLM clients do not receive secrets or PII.
- Given configurable field names and regex patterns (tokens, passwords, emails, card numbers, auth headers), when results are returned, then matches are replaced by `[REDACTED:<type>]`.
- Redaction applies to message bodies, attributes and stack traces, and is covered by tests with sample log lines.

**OBS-SAFE-05 · Prompt-injection hardening · M · v1**
As a *Security Officer*, I want log content marked as untrusted data so that instructions hidden in logs are not followed by the AI client.
- Given any result containing log/trace text, when returned, then the text sits in clearly named data fields and the tool descriptions state that content is untrusted.
- The server never executes or follows instructions found in telemetry content.

**OBS-SAFE-06 · Audit log · S · v1**
As a *Security Officer*, I want every tool call recorded (who, tool, parameters summary, index patterns, result count, duration, outcome) so that access to telemetry is auditable.
- Given a tool call, when it completes, then a structured audit event is emitted without result content or secrets.
- Audit events carry the caller identity and correlation ID.

### E8 — Resilience and performance (`RES`)

**OBS-RES-01 · Retries, timeouts and circuit breaker · M · MVP**
As a *Platform / SRE*, I want transient Elastic failures handled so that the server degrades gracefully.
- Given a `429`/`502`/`503`/`504` or network error, when a query fails, then it is retried with exponential backoff and jitter within the call's total time budget.
- Given sustained failure, when the breaker opens, then tools return `upstream_unavailable` immediately and the breaker half-opens after the cool-down.

**OBS-RES-02 · Rate limiting and fair use · S · v1**
As a *Platform / SRE*, I want per-caller and global rate limits so that one chatty agent cannot starve others or the cluster.
- Given a caller exceeds the limit, when it calls a tool, then it receives `rate_limited` with `retry_after`.

**OBS-RES-03 · Metadata caching · S · v1**
As a *Platform / SRE*, I want field capabilities, index lists and service lists cached briefly so that repeated discovery calls are cheap.
- Given a cached entry, when requested within its TTL (default 5 min), then Elastic is not called; the cache is bounded and never stores log content.

**OBS-RES-04 · Pagination with stable cursors · S · v1**
As an *AI Agent*, I want to page through large result sets so that I can read beyond the first page without duplicates.
- Given a page of results, when a `next_cursor` is returned and passed back, then the next page continues from the same snapshot (PIT + `search_after`).
- Given an expired cursor, when used, then a clear `cursor_expired` error is returned.

**OBS-RES-05 · Cancellation and graceful shutdown · M · MVP**
As a *Platform / SRE*, I want in-flight queries cancelled when the client disconnects or the server stops so that no work is wasted.
- Given a cancelled MCP request, when it is cancelled, then the Elastic request is aborted and any PIT is closed.
- Given SIGTERM, when received, then in-flight calls drain within the shutdown timeout.

### E9 — Server telemetry (`TEL`)

**OBS-TEL-01 · Emit own traces, metrics and logs via OTLP · M · MVP**
As a *Platform / SRE*, I want the server to export its own OpenTelemetry signals so that I can operate it like any other service.
- Given an OTLP endpoint is configured, when the server runs, then traces for each tool call, Elastic request and cache lookup, plus metrics and structured logs, are exported with `service.name=mcp-observability`.
- Query text values and log content are never attached to spans or logs.

**OBS-TEL-02 · Tool-call metrics · M · MVP**
As a *Platform / SRE*, I want call count, latency, error rate, truncation rate and Elastic round-trip time per tool so that I can set SLOs.
- Given tool calls, when they complete, then metrics are recorded with tool name and outcome as dimensions (no high-cardinality values).

**OBS-TEL-03 · Health and readiness endpoints · M · MVP**
As a *Platform / SRE*, I want liveness and readiness probes so that orchestrators can manage the server.
- `/health/live` returns OK when the process runs; `/health/ready` reflects Elasticsearch (and Kibana if configured) reachability and the circuit-breaker state.
- Probe responses contain no version, host or credential detail.

**OBS-TEL-04 · Correlation IDs · S · v1**
As a *Platform / SRE*, I want one correlation ID per tool call across server logs, audit events and the Elastic `X-Opaque-Id` header so that I can trace a call through Elastic slow logs.
- Given a tool call, when it reaches Elastic, then the request carries `X-Opaque-Id`; the same ID is in the server's logs and the tool error payload.

### E10 — MCP surface (`MCP`)

**OBS-MCP-01 · Typed tools with output schemas and accurate descriptions · M · MVP**
As an *AI Agent*, I want each tool to have a precise description, an input schema with units/defaults and a typed output schema so that I choose and call tools correctly.
- Every tool declares read-only/idempotent annotations and documents its caps and defaults.
- A contract test fails if a tool lacks a description, schema or annotation.

**OBS-MCP-02 · Structured, actionable errors · M · MVP**
As an *AI Agent*, I want errors with a stable code, a safe message and a suggested next step so that I can recover without guessing.
- Error codes include `invalid_argument`, `not_found`, `forbidden`, `auth_required`, `rate_limited`, `upstream_unavailable`, `timeout`, `cursor_expired`.
- Errors never include credentials, raw queries or Elastic internals.

**OBS-MCP-03 · Resources · C · Later**
As an *AI Agent*, I want read-only resources (e.g. field catalog, service catalog, mapping profile) so that I can load context without a tool call.
- Given a client lists resources, when it reads `obs://services` or `obs://fields/logs`, then bounded, cached content is returned.

**OBS-MCP-04 · Investigation prompts · S · v1**
As an *On-call / Developer*, I want ready-made prompts such as "investigate error spike", "why is this trace slow" and "compare before/after deploy" so that my assistant follows a sound procedure.
- Given a prompt is selected with arguments (service, time window), when rendered, then it guides the agent to use the tools in a sensible order and to cite evidence (trace IDs, log IDs, Kibana links).

### E11 — Packaging and operations (`OPS`)

**OBS-OPS-01 · stdio and Streamable HTTP transports · M · MVP**
As a *Developer*, I want to run the server locally over stdio and remotely over Streamable HTTP so that it fits both desktop and shared deployments.
- Given a transport option, when the server starts, then it exposes only that transport; stdio never writes logs to stdout.

**OBS-OPS-02 · Container image · M · v1**
As a *Platform / SRE*, I want a small, non-root, read-only-filesystem image so that deployment is secure by default.
- The image builds from a pinned base, runs as a non-root user, has no shell-time secrets, and passes a vulnerability scan in CI.

**OBS-OPS-03 · Configuration validation and documentation · M · MVP**
As an *Elastic Admin*, I want one documented configuration model validated at startup so that mistakes are found immediately.
- Given invalid or missing options, when the server starts, then every problem is reported at once and the process exits non-zero.
- ReadMe documents all `Elastic__*`, `Kibana__*` and `Observability__*` environment variables and the minimum Elastic role.

**OBS-OPS-04 · CI pipeline and quality gates · M · MVP**
As a *Technical Lead*, I want build, tests, architecture tests, vulnerability and secret scanning on every PR so that quality and safety are enforced.
- Given a pull request, when CI runs, then any warning, failed test, architecture violation or detected secret blocks merge.

**OBS-OPS-05 · Versioning, tool catalog and changelog · S · v1**
As a *Developer*, I want a generated tool catalog and semantic versioning so that I can see what changed between releases.
- Given a release, when published, then the tool catalog (names, inputs, outputs, caps) and changelog are generated from code; breaking tool changes bump the major version.

**OBS-OPS-06 · Runbook and SLOs · S · v1**
As a *Platform / SRE*, I want a runbook and defined SLOs so that incidents are handled consistently.
- The runbook covers auth failures, breaker open, Elastic slowness and rate-limit tuning; SLOs reference the metrics from OBS-TEL-02.

---

## 6. Non-functional requirements

| Area | Requirement |
|---|---|
| **Latency** | p95 tool latency ≤ 3 s for searches over ≤ 1 h; ≤ 10 s for ≤ 24 h aggregations (excluding Elastic outage). |
| **Output size** | Default ≤ 50 records / ~8 K tokens per response; hard cap 500 records. |
| **Availability** | Stateless; horizontally scalable; ≥ 99.5 % for the server itself (Elastic availability excluded). |
| **Security** | Read-only by construction; TLS only; least-privilege Elastic role; no secrets or log content in server logs; redaction on by default. |
| **Compatibility** | Elastic 8.x/9.x; MCP spec version pinned and documented; .NET 10. |
| **Maintainability** | Clean Architecture layers enforced by architecture tests; Elastic and Kibana behind `BuildingBlock` interfaces so other providers can be added. |
| **Testability** | Unit tests with fakes; integration tests against a containerized Elasticsearch with seeded OTLP-shaped data. |

## 7. Dependencies and risks

| Item | Note |
|---|---|
| OTLP field layout varies | Mitigated by mapping profiles (OBS-OTEL-02); validate against real data early. |
| Log content is untrusted and sensitive | Redaction (OBS-SAFE-04) and prompt-injection hardening (OBS-SAFE-05) are MVP/v1 gates. |
| Expensive queries | Guardrails (OBS-SAFE-03) must ship in MVP, before any aggregation tool. |
| Per-user RBAC | Needs Elastic-side support for delegated credentials (Q4); decision affects remote hosting. |
| Elastic API differences | Cloud/Serverless vs self-managed; verify with integration tests on both. |

## 8. Release plan

| Release | Goal | Stories |
|---|---|---|
| **MVP** | A developer can connect to Elastic, find logs and traces safely, locally. | AUTH-01, AUTH-03, AUTH-04, AUTH-05, LOG-01, LOG-02, LOG-03, LOG-07, TRACE-01, TRACE-02, OTEL-01, OTEL-02, SAFE-01, SAFE-02, SAFE-03, SAFE-04, RES-01, RES-05, TEL-01, TEL-02, TEL-03, MCP-01, MCP-02, OPS-01, OPS-03, OPS-04 |
| **v1** | Metrics, correlation, remote multi-user hosting and operational maturity. | AUTH-02, AUTH-06, LOG-04, LOG-05, LOG-06, TRACE-03, TRACE-04, TRACE-05, METRIC-01, METRIC-02, METRIC-03, OTEL-03, OTEL-04, KIB-01, KIB-02, SAFE-05, SAFE-06, RES-02, RES-03, RES-04, TEL-04, MCP-04, OPS-02, OPS-05, OPS-06 |
| **Later** | Nice-to-haves. | METRIC-04, KIB-03, KIB-04, MCP-03 |

Suggested build order inside MVP: AUTH → SAFE (01–03) → OTEL-02 → LOG-01/07 → MCP-01/02 → TRACE → SAFE-04 → RES → TEL → OPS.

## 9. Story count

11 epics, 55 stories: 26 MVP, 25 v1, 4 Later.
