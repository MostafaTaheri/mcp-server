# Jira MCP Server — User Stories

**Prepared for:** Mostafa Taheri — Technical Lead | Software Architect
**Status:** Draft v0.1 for review
**Solution:** `MCPServer` (.NET 10, Clean Architecture)

## 1. Purpose and scope

Build a production-grade [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that lets AI clients (Claude Desktop, Claude Code, IDE agents, custom agents) read and change Jira data safely. Jira is the first integration; the design must allow further integrations (Confluence, GitHub, etc.) without reworking the core.

**In scope (Jira):** Jira Cloud REST API v3 and Jira Software (Agile) API; MCP tools, resources and prompts; authentication; safety controls; resilience; observability; packaging and operations.

**Out of scope for this phase:** Jira Data Center / Server (planned, see Q1), Jira Service Management queues, Jira admin operations (workflows, schemes, permissions), webhook-driven push notifications, a UI.

## 2. Personas

| Persona | Description |
|---|---|
| **AI Agent / MCP Client** | The LLM-driven client that calls tools. Needs predictable schemas, bounded output and actionable errors. |
| **Developer / End User** | Person who connects their AI assistant to Jira to query and update work items. |
| **Jira Admin** | Controls which projects, scopes and credentials the server may use. |
| **Platform / SRE** | Deploys, monitors and operates the server. |
| **Security / Compliance Officer** | Needs least privilege, auditability, data-protection guarantees. |

## 3. Assumptions and open questions

| # | Assumption (default used in these stories) | Question to confirm |
|---|---|---|
| Q1 | Target is **Jira Cloud** (REST v3). | Do we also need Data Center / Server? |
| Q2 | Both **stdio** (local) and **Streamable HTTP** (remote) transports are supported. | Is remote hosting required for MVP? |
| Q3 | Auth is **API token** (local use) and **OAuth 2.0 (3LO)** (remote/multi-user). | Is SSO/OIDC in front of the HTTP transport mandatory? |
| Q4 | One server instance may serve **multiple Jira sites**, but each request acts as **one user identity**. | Do we need per-tenant isolation in one process? |
| Q5 | The server is **stateless**; cache is in-memory (optional distributed cache later). | Is Redis acceptable as an infrastructure dependency? |
| Q6 | Writes are allowed but **disabled by default** (read-only mode on). | Which write tools are acceptable for production? |

## 4. Story conventions

- **ID:** `JIRA-<EPIC>-<nn>`.
- **Priority (MoSCoW):** M = Must, S = Should, C = Could.
- **Release:** MVP → v1 → Later (see section 8).
- **Acceptance criteria** use Given/When/Then, abbreviated as bullets.
- **Definition of Done (all stories):** code reviewed; unit tests added; architecture tests pass; no secrets or issue content in logs; docs/tool catalog updated; build has no warnings (`TreatWarningsAsErrors`).

---

## 5. Epics and stories

### E1 — Connectivity and authentication (`AUTH`)

**JIRA-AUTH-01 · API token authentication · M · MVP**
As a *Developer*, I want to connect with my Atlassian email and API token so that I can start using the server locally in minutes.
- Given valid site URL, email and token in configuration, when the server starts, then it verifies identity via `/myself` and logs the connected account ID (not the token).
- Given invalid credentials, when the server starts, then it fails fast with a clear, non-leaking error.

**JIRA-AUTH-02 · OAuth 2.0 (3LO) authorization · M · v1**
As a *Developer*, I want to authorize via OAuth so that the server acts with my own Jira permissions without sharing a token.
- Given an unauthorized HTTP client, when it calls a tool, then it receives an authorization challenge that starts the OAuth flow (PKCE).
- Given a completed flow, when tools are called, then Jira requests carry that user's access token only.

**JIRA-AUTH-03 · Token refresh and expiry handling · M · v1**
As an *AI Agent*, I want expired access tokens renewed transparently so that long sessions do not fail mid-task.
- Given an access token near expiry, when a tool is called, then it is refreshed once (single-flight) before the call.
- Given a revoked refresh token, when refresh fails, then the tool returns a structured `auth_required` error.

**JIRA-AUTH-04 · Secure secret handling · M · MVP**
As a *Security Officer*, I want credentials loaded from environment variables or a secret store so that no secret is committed or logged.
- Secrets are never written to logs, traces, error messages or tool results.
- Configuration supports env vars and a pluggable secret provider (e.g. Key Vault, Docker/K8s secrets).
- OAuth tokens at rest are encrypted.

**JIRA-AUTH-05 · Least-privilege scopes · S · v1**
As a *Jira Admin*, I want the server to request only the scopes it needs so that a leaked token has limited reach.
- Scopes are derived from the enabled tool set (read-only mode requests read scopes only).
- Documentation lists the scope required by each tool.

**JIRA-AUTH-06 · Multi-site configuration · C · Later**
As a *Developer*, I want to configure several Jira sites and pick one per call so that I can work across organisations.
- Given two configured sites, when a tool omits `site`, then the default site is used.
- Given an unknown site alias, then a validation error lists valid aliases.

**JIRA-AUTH-07 · Connection health tool · S · MVP**
As a *Developer*, I want a `jira_check_connection` tool so that I can diagnose connectivity and permission problems.
- Returns site, authenticated user, server time and read/write capability.
- Never returns credentials.

---

### E2 — Issue retrieval (`READ`)

**JIRA-READ-01 · Get issue by key · M · MVP**
As an *AI Agent*, I want to fetch an issue by key so that I can reason about its current state.
- Given `PROJ-123`, then summary, status, assignee, reporter, priority, labels, description (as readable text), created/updated are returned.
- Given a non-existent or inaccessible key, then a `not_found` error is returned without revealing whether it exists.

**JIRA-READ-02 · Search with JQL · M · MVP**
As an *AI Agent*, I want to search with JQL so that I can find sets of issues.
- Results are paginated with a cursor/`nextPageToken`; default page size 25, max 100.
- Invalid JQL returns Jira's error message in a structured `invalid_query` error.
- Only requested fields are returned to limit tokens.

**JIRA-READ-03 · Natural-language search helper · C · Later**
As a *Developer*, I want common searches (my open issues, sprint issues, recently updated) exposed as simple parameters so that the agent need not write JQL.
- Parameters are translated to JQL server-side with all values escaped.
- The generated JQL is returned for transparency.

**JIRA-READ-04 · Read comments · M · MVP**
As an *AI Agent*, I want an issue's comments so that I can understand discussion context.
- Comments are paginated, ordered oldest-first by default, with author, timestamp and plain-text body.

**JIRA-READ-05 · Read changelog / history · S · v1**
As a *Developer*, I want status and field history so that I can answer "when did this change and who changed it".
- Returns field, from/to, author, timestamp; paginated.

**JIRA-READ-06 · Linked issues and sub-tasks · S · v1**
As an *AI Agent*, I want issue links, parent and sub-tasks so that I can navigate dependencies.
- Returns link type, direction and target key/summary/status.
- Depth is limited to one level per call.

**JIRA-READ-07 · Attachment metadata and download · C · v1**
As a *Developer*, I want attachment names, sizes and (optionally) content so that I can review evidence.
- Metadata returned by default; content only for allowed types under a configurable size limit (default 1 MB).

**JIRA-READ-08 · Rich-text conversion · M · MVP**
As an *AI Agent*, I want Jira's Atlassian Document Format converted to Markdown/plain text so that content is token-efficient and readable.
- Headings, lists, code blocks, links and mentions are preserved; unsupported nodes degrade to text, never fail.

---

### E3 — Issue changes (`WRITE`)

All write stories are gated by safety controls in E7 (disabled unless write mode is enabled).

**JIRA-WRITE-01 · Create issue · M · v1**
As a *Developer*, I want to create an issue so that the agent can file work items.
- Required: project, issue type, summary; optional: description (Markdown → ADF), assignee, labels, priority, components, custom fields by ID.
- Missing required fields per Jira createmeta return a validation error listing them.
- Returns the new key and URL.

**JIRA-WRITE-02 · Update issue fields · M · v1**
As a *Developer*, I want to edit fields so that the agent can keep issues accurate.
- Only supplied fields change; unknown fields are rejected with the list of editable fields.
- Response includes old and new values.

**JIRA-WRITE-03 · Transition issue (workflow-aware) · M · v1**
As a *Developer*, I want to move an issue through its workflow so that status reflects reality.
- Available transitions can be listed; transitioning to an unavailable one returns the valid options.
- Required transition fields (e.g. resolution) are validated before the call.

**JIRA-WRITE-04 · Add comment · M · v1**
As a *Developer*, I want to add a comment so that the agent can report findings.
- Markdown is converted to ADF; comment visibility restriction is optional.
- Comment is attributed to the authenticated user, with an optional configurable "posted via AI" footer.

**JIRA-WRITE-05 · Edit / delete own comment · C · Later**
As a *Developer*, I want to correct a comment so that mistakes can be fixed.
- Only comments authored by the authenticated user can be edited/deleted (delete needs confirmation).

**JIRA-WRITE-06 · Assign issue · S · v1**
As a *Developer*, I want to assign or unassign an issue so that ownership is clear.
- Assignee resolved by account ID or unique email/display name; ambiguous matches return candidates instead of guessing.

**JIRA-WRITE-07 · Link issues · S · v1**
As a *Developer*, I want to link two issues so that dependencies are recorded.
- Link type is validated against available link types; duplicate links are idempotent.

**JIRA-WRITE-08 · Log work · S · v1**
As a *Developer*, I want to add a worklog so that time is tracked.
- Accepts duration (`1h 30m`) and start time; invalid durations are rejected with an example.

**JIRA-WRITE-09 · Upload attachment · C · Later**
As a *Developer*, I want to attach a file so that evidence is kept with the issue.
- Size and type limits enforced; files scanned/validated per policy.

**JIRA-WRITE-10 · Delete issue (guarded) · C · Later**
As a *Developer*, I want deletion available but protected so that accidental data loss is prevented.
- Disabled by default; requires explicit allow-listing and a confirmation token (see SAFE-04).

---

### E4 — Projects and metadata (`META`)

**JIRA-META-01 · List projects · M · MVP**
As an *AI Agent*, I want to list accessible projects so that I can choose valid keys.
- Paginated, filterable by name/key; respects the project allow-list (SAFE-03).

**JIRA-META-02 · Project details · S · MVP**
As an *AI Agent*, I want project issue types, components and versions so that creation requests are valid.
- Returns issue types, components, versions and lead for one project.

**JIRA-META-03 · Field catalogue and create metadata · M · v1**
As an *AI Agent*, I want required and allowed fields per project/issue type so that I send correct payloads.
- Includes field ID, name, type, required flag and allowed values.
- Result is cached (see REL-06).

**JIRA-META-04 · Statuses, priorities, resolutions · S · MVP**
As an *AI Agent*, I want valid status/priority/resolution values so that I can build accurate queries and updates.

**JIRA-META-05 · User lookup · S · MVP**
As an *AI Agent*, I want to find users by name/email so that I can assign or mention them.
- Returns account ID, display name and (if permitted) email; honours privacy settings.

**JIRA-META-06 · Saved filters · C · Later**
As a *Developer*, I want to list and run saved filters so that team-agreed queries are reused.

---

### E5 — Agile boards and sprints (`AGILE`)

**JIRA-AGILE-01 · List boards · S · v1**
As a *Developer*, I want to list boards for a project so that I can find sprint context.

**JIRA-AGILE-02 · List and read sprints · S · v1**
As a *Developer*, I want active, future and closed sprints with dates and goals so that I can report on progress.

**JIRA-AGILE-03 · Sprint issues and backlog · S · v1**
As a *Developer*, I want issues in a sprint or the backlog so that I can plan.
- Paginated; supports status filter.

**JIRA-AGILE-04 · Move issues to sprint · C · v1**
As a *Developer*, I want to move issues into a sprint so that the agent can help with planning.
- Rejected for closed sprints; max 50 issues per call; write-mode gated.

**JIRA-AGILE-05 · Epic overview · C · Later**
As a *Developer*, I want an epic's children and progress summary so that I can report delivery status.

---

### E6 — MCP protocol surface (`MCP`)

**JIRA-MCP-01 · Compliant MCP server · M · MVP**
As an *MCP Client*, I want a spec-compliant server so that any conforming client can connect.
- Handles `initialize`, capability negotiation, `tools/list`, `tools/call`, cancellation and ping.
- Passes MCP Inspector checks (see QA-04).

**JIRA-MCP-02 · stdio transport · M · MVP**
As a *Developer*, I want to run the server over stdio so that local clients work with zero networking.
- Nothing except protocol messages is written to stdout; logs go to stderr/file.

**JIRA-MCP-03 · Streamable HTTP transport · M · v1**
As a *Platform engineer*, I want a remote HTTP endpoint so that teams share one deployment.
- TLS required outside localhost; origin validation; per-session isolation of credentials.

**JIRA-MCP-04 · Well-designed tool schemas · M · MVP**
As an *AI Agent*, I want clear tool names, descriptions and JSON Schemas so that I pick and call tools correctly.
- Each tool has a verb-first name (`jira_search_issues`), concise description, typed/validated parameters, examples, and read-only/destructive hints (annotations).
- Tool descriptions are reviewed for token cost (< 120 words each).

**JIRA-MCP-05 · Structured errors · M · MVP**
As an *AI Agent*, I want errors with a stable code, message and remediation hint so that I can recover.
- Codes include `auth_required`, `forbidden`, `not_found`, `invalid_input`, `rate_limited`, `upstream_unavailable`, `blocked_by_policy`.
- Internal exceptions never leak stack traces or upstream response bodies.

**JIRA-MCP-06 · Bounded, paginated output · M · MVP**
As an *AI Agent*, I want responses capped in size so that my context window is not flooded.
- Configurable max response size (default ~20k chars); truncated results state what was cut and how to fetch more.

**JIRA-MCP-07 · Resources · S · v1**
As an *MCP Client*, I want issues and projects exposed as resources (`jira://issue/PROJ-1`) so that users can attach them as context.

**JIRA-MCP-08 · Prompts · C · v1**
As a *Developer*, I want ready-made prompts (triage issue, summarize sprint, draft release notes) so that common workflows are one click.

---

### E7 — Safety and authorization (`SAFE`)

**JIRA-SAFE-01 · Read-only mode by default · M · MVP**
As a *Security Officer*, I want write tools hidden and rejected unless explicitly enabled so that the default is safe.
- With read-only on, write tools are absent from `tools/list` and calls return `blocked_by_policy`.

**JIRA-SAFE-02 · Tool allow/deny lists · M · MVP**
As a *Jira Admin*, I want to enable only specific tools so that exposure is minimal.
- Configuration by tool name or group; deny wins over allow.

**JIRA-SAFE-03 · Project allow-list · M · v1**
As a *Jira Admin*, I want to restrict the server to certain projects so that it cannot touch others.
- Applies to reads, searches (JQL is constrained by project clause) and writes.

**JIRA-SAFE-04 · Confirmation for destructive actions · S · v1**
As a *Developer*, I want destructive or bulk actions to require explicit confirmation so that the agent cannot act on a hallucination.
- First call returns a preview and confirmation token (short TTL, bound to arguments); the action runs only when the same token is resubmitted.

**JIRA-SAFE-05 · Dry-run for writes · S · v1**
As a *Developer*, I want a `dryRun` option that validates and shows the intended change without applying it.

**JIRA-SAFE-06 · Input validation and injection defence · M · MVP**
As a *Security Officer*, I want all inputs validated so that malformed or malicious arguments cannot reach Jira.
- Keys, IDs, enums, lengths and JQL fragments validated; values escaped; unexpected properties rejected.

**JIRA-SAFE-07 · Prompt-injection mitigation for Jira content · M · v1**
As a *Security Officer*, I want issue text treated as untrusted data so that instructions hidden in tickets cannot hijack the agent.
- Returned user-authored content is clearly delimited and labelled as untrusted.
- Write tools never accept arguments sourced from tool output without the client's own decision (documented guidance + tests with injection fixtures).

**JIRA-SAFE-08 · Sensitive-data redaction · S · v1**
As a *Compliance Officer*, I want configurable redaction (emails, tokens, patterns) so that sensitive data is not sent to the model unnecessarily.
- Redaction rules configurable; applied to results and logs.

---

### E8 — Reliability and performance (`REL`)

**JIRA-REL-01 · Rate-limit handling · M · MVP**
As a *Platform engineer*, I want HTTP 429/`Retry-After` respected so that we are never blocked by Atlassian.
- Waits up to a bounded time, then returns `rate_limited` with retry-after seconds.

**JIRA-REL-02 · Retries with backoff and jitter · M · MVP**
As an *AI Agent*, I want transient failures retried so that brief outages are invisible.
- Only idempotent calls (GET and safe PUT) retried; max attempts and total budget configurable; 4xx (except 429/408) never retried.

**JIRA-REL-03 · Timeouts and deadlines · M · MVP**
As a *Platform engineer*, I want per-call and overall deadlines so that no tool call hangs.
- Cancellation from the client propagates to the Jira request.

**JIRA-REL-04 · Circuit breaker and bulkhead · S · v1**
As a *Platform engineer*, I want to stop calling a failing Jira and limit concurrency so that we protect both sides.
- Open circuit returns `upstream_unavailable` immediately; concurrency limit per site configurable.

**JIRA-REL-05 · Idempotent writes · S · v1**
As a *Developer*, I want retried or repeated write calls not to create duplicates so that results are predictable.
- Create/comment/link support an idempotency key; duplicates within TTL return the original result.

**JIRA-REL-06 · Metadata caching · S · v1**
As a *Platform engineer*, I want rarely-changing metadata cached so that latency and API usage fall.
- TTL per data type; invalidated on related writes; cache never holds per-user data across users.

**JIRA-REL-07 · Graceful degradation · S · v1**
As a *Developer*, I want partial results with warnings rather than total failure so that long operations stay useful.
- If one sub-call (e.g. changelog) fails, the rest is returned with a `warnings` entry.

**JIRA-REL-08 · Performance targets · S · v1**
As a *Developer*, I want fast responses so that the agent loop feels responsive.
- Server overhead p95 < 100 ms excluding Jira latency; memory stable under 100 concurrent sessions (verified by load test QA-05).

---

### E9 — Observability and audit (`OBS`)

**JIRA-OBS-01 · Structured logging without content · M · MVP**
As an *SRE*, I want JSON logs with correlation IDs so that I can troubleshoot without exposing data.
- Logs include tool name, duration, outcome, upstream status; exclude issue content, tokens and PII.

**JIRA-OBS-02 · Distributed tracing · S · v1**
As an *SRE*, I want OpenTelemetry traces covering MCP request → tool → Jira call so that I can find slow stages.
- W3C trace context propagated; exporter configurable (OTLP).

**JIRA-OBS-03 · Metrics · S · v1**
As an *SRE*, I want request count, latency, error rate, retry count, rate-limit hits and cache hit ratio so that I can alert on SLOs.

**JIRA-OBS-04 · Health and readiness endpoints · M · v1**
As a *Platform engineer*, I want `/health/live` and `/health/ready` (HTTP mode) so that orchestrators manage the container.
- Readiness reflects ability to reach Jira and valid configuration; liveness does not depend on Jira.

**JIRA-OBS-05 · Audit trail of write actions · M · v1**
As a *Compliance Officer*, I want an immutable record of every write (who, when, tool, target, outcome) so that changes are attributable.
- Contains argument hashes/field names, not full content; sink configurable.

**JIRA-OBS-06 · Usage-attribution header · C · Later**
As a *Jira Admin*, I want requests identifiable as coming from the MCP server (User-Agent with version) so that API usage can be traced in Atlassian logs.

---

### E10 — Operations and delivery (`OPS`)

**JIRA-OPS-01 · Container image · M · v1**
As a *Platform engineer*, I want a minimal, non-root, reproducible Docker image so that deployment is consistent.
- Multi-stage build; read-only filesystem works; image scanned with no critical vulnerabilities.

**JIRA-OPS-02 · Configuration model · M · MVP**
As a *Platform engineer*, I want typed, validated configuration (appsettings + env overrides) so that mistakes fail at startup.
- Invalid or missing settings produce a single clear startup error listing all problems.

**JIRA-OPS-03 · CI pipeline · M · MVP**
As a *Developer*, I want build, test, format and vulnerability checks on each change so that quality gates are enforced.
- Pipeline fails on warnings, failing tests, architecture-rule violations, or vulnerable packages.

**JIRA-OPS-04 · Release and versioning · S · v1**
As a *Platform engineer*, I want semantic versioning with changelog and signed artifacts so that upgrades are predictable.
- Version exposed in `initialize` server info and User-Agent.

**JIRA-OPS-05 · Graceful shutdown · S · v1**
As a *Platform engineer*, I want in-flight calls to complete (bounded) on SIGTERM so that deployments do not drop work.

**JIRA-OPS-06 · Supply-chain security · S · v1**
As a *Security Officer*, I want an SBOM and automated dependency updates so that we can respond quickly to CVEs.

**JIRA-OPS-07 · Runbook · S · v1**
As an *SRE*, I want a runbook covering common alerts, credential rotation and rollback so that incidents are resolved quickly.

---

### E11 — Quality assurance (`QA`)

**JIRA-QA-01 · Unit tests for tools and mapping · M · MVP**
As a *Developer*, I want unit tests for validation, ADF conversion, JQL escaping and error mapping so that logic regressions are caught.
- Target ≥ 80% line coverage on Application and Domain layers.

**JIRA-QA-02 · Integration tests with Jira stub · M · MVP**
As a *Developer*, I want tests against a recorded/mock Jira API (e.g. WireMock) so that behaviour including 429, 5xx and timeouts is exercised deterministically.

**JIRA-QA-03 · Architecture tests · M · MVP**
As a *Technical Lead*, I want layer-dependency rules enforced in `MCPServer.ArchTests` so that Clean Architecture boundaries hold.
- Domain has no outward dependencies; Application does not reference Infrastructure; MCP types stay in the Api layer.

**JIRA-QA-04 · MCP conformance tests · S · v1**
As a *Developer*, I want automated protocol checks (MCP Inspector / SDK client) so that spec compliance is verified in CI.

**JIRA-QA-05 · Load and soak tests · S · v1**
As an *SRE*, I want load and soak tests so that REL-08 targets and leak-free operation are demonstrated.

---

### E12 — Documentation and developer experience (`DOC`)

**JIRA-DOC-01 · Quick-start · M · MVP**
As a *Developer*, I want a 5-minute guide with copy-paste config for Claude Desktop and Claude Code so that onboarding is easy.

**JIRA-DOC-02 · Tool catalogue · M · MVP**
As an *AI Agent builder*, I want every tool documented with parameters, examples, required scopes and safety class so that I can integrate confidently.

**JIRA-DOC-03 · Security and threat model · S · v1**
As a *Security Officer*, I want a documented threat model (prompt injection, token theft, over-broad access) with mitigations mapped to stories so that reviews are efficient.

**JIRA-DOC-04 · Troubleshooting guide · S · v1**
As a *Developer*, I want a list of error codes with causes and fixes so that I can self-serve.

---

## 6. Non-functional requirements

| Area | Requirement |
|---|---|
| Security | OWASP ASVS L2 aligned; secrets never logged; TLS 1.2+ for remote; dependencies scanned each build. |
| Privacy | No persistence of Jira content; logs and traces content-free; supports data-subject deletion by design (nothing stored). |
| Availability | HTTP mode targets 99.9% monthly excluding Jira outages; stateless, horizontally scalable. |
| Performance | See REL-08; response size bounded (MCP-06). |
| Compatibility | Latest MCP spec revision at release; Jira Cloud REST v3 and Agile API; .NET 10 runtime. |
| Maintainability | Clean Architecture boundaries enforced by tests; new integrations added as separate modules. |
| Cost / quota | API calls minimised through caching and field selection; rate-limit aware. |

## 7. Risks

| Risk | Mitigation (stories) |
|---|---|
| Prompt injection via ticket content | SAFE-07, SAFE-04, MCP-04 annotations |
| Unintended or runaway writes | SAFE-01/02/03/04/05, OBS-05 |
| Atlassian rate limiting or API changes | REL-01/02/04, QA-02, contract tests |
| Credential leakage | AUTH-04, OBS-01, SAFE-08 |
| Context-window overload | MCP-06, READ-02 field selection |

## 8. Release slicing

| Release | Goal | Epics / stories |
|---|---|---|
| **MVP** | Safe, read-only Jira access over stdio with API token | AUTH-01/04/07; READ-01/02/04/08; META-01/02/04/05; MCP-01/02/04/05/06; SAFE-01/02/06; REL-01/02/03; OBS-01; OPS-02/03; QA-01/02/03; DOC-01/02 |
| **v1** | Controlled writes, agile, remote HTTP+OAuth, full resilience and observability | Remaining Must/Should stories not marked Later |
| **Later** | Convenience and breadth | AUTH-06, READ-03, WRITE-05/09/10, META-06, AGILE-05, OBS-06 |

## 9. Traceability

| ID | Title | Pri | Release |
|---|---|:-:|:-:|
| AUTH-01 | API token authentication | M | MVP |
| AUTH-02 | OAuth 2.0 (3LO) | M | v1 |
| AUTH-03 | Token refresh | M | v1 |
| AUTH-04 | Secret handling | M | MVP |
| AUTH-05 | Least-privilege scopes | S | v1 |
| AUTH-06 | Multi-site | C | Later |
| AUTH-07 | Connection health tool | S | MVP |
| READ-01 | Get issue | M | MVP |
| READ-02 | JQL search | M | MVP |
| READ-03 | Simple search helper | C | Later |
| READ-04 | Comments | M | MVP |
| READ-05 | Changelog | S | v1 |
| READ-06 | Links and sub-tasks | S | v1 |
| READ-07 | Attachments | C | v1 |
| READ-08 | ADF conversion | M | MVP |
| WRITE-01 | Create issue | M | v1 |
| WRITE-02 | Update fields | M | v1 |
| WRITE-03 | Transition | M | v1 |
| WRITE-04 | Add comment | M | v1 |
| WRITE-05 | Edit/delete comment | C | Later |
| WRITE-06 | Assign | S | v1 |
| WRITE-07 | Link issues | S | v1 |
| WRITE-08 | Log work | S | v1 |
| WRITE-09 | Upload attachment | C | Later |
| WRITE-10 | Delete issue | C | Later |
| META-01 | List projects | M | MVP |
| META-02 | Project details | S | MVP |
| META-03 | Field catalogue | M | v1 |
| META-04 | Statuses/priorities | S | MVP |
| META-05 | User lookup | S | MVP |
| META-06 | Saved filters | C | Later |
| AGILE-01 | Boards | S | v1 |
| AGILE-02 | Sprints | S | v1 |
| AGILE-03 | Sprint issues/backlog | S | v1 |
| AGILE-04 | Move to sprint | C | v1 |
| AGILE-05 | Epic overview | C | Later |
| MCP-01 | Compliant server | M | MVP |
| MCP-02 | stdio transport | M | MVP |
| MCP-03 | Streamable HTTP | M | v1 |
| MCP-04 | Tool schemas | M | MVP |
| MCP-05 | Structured errors | M | MVP |
| MCP-06 | Bounded output | M | MVP |
| MCP-07 | Resources | S | v1 |
| MCP-08 | Prompts | C | v1 |
| SAFE-01 | Read-only default | M | MVP |
| SAFE-02 | Tool allow/deny | M | MVP |
| SAFE-03 | Project allow-list | M | v1 |
| SAFE-04 | Confirmation | S | v1 |
| SAFE-05 | Dry-run | S | v1 |
| SAFE-06 | Input validation | M | MVP |
| SAFE-07 | Prompt-injection mitigation | M | v1 |
| SAFE-08 | Redaction | S | v1 |
| REL-01 | Rate limits | M | MVP |
| REL-02 | Retries | M | MVP |
| REL-03 | Timeouts | M | MVP |
| REL-04 | Circuit breaker | S | v1 |
| REL-05 | Idempotency | S | v1 |
| REL-06 | Caching | S | v1 |
| REL-07 | Degradation | S | v1 |
| REL-08 | Performance targets | S | v1 |
| OBS-01 | Structured logs | M | MVP |
| OBS-02 | Tracing | S | v1 |
| OBS-03 | Metrics | S | v1 |
| OBS-04 | Health endpoints | M | v1 |
| OBS-05 | Audit trail | M | v1 |
| OBS-06 | Usage attribution | C | Later |
| OPS-01 | Container image | M | v1 |
| OPS-02 | Configuration | M | MVP |
| OPS-03 | CI pipeline | M | MVP |
| OPS-04 | Versioning/release | S | v1 |
| OPS-05 | Graceful shutdown | S | v1 |
| OPS-06 | Supply chain | S | v1 |
| OPS-07 | Runbook | S | v1 |
| QA-01 | Unit tests | M | MVP |
| QA-02 | Integration tests | M | MVP |
| QA-03 | Architecture tests | M | MVP |
| QA-04 | MCP conformance | S | v1 |
| QA-05 | Load/soak | S | v1 |
| DOC-01 | Quick-start | M | MVP |
| DOC-02 | Tool catalogue | M | MVP |
| DOC-03 | Threat model | S | v1 |
| DOC-04 | Troubleshooting | S | v1 |

**Totals:** 82 stories — 39 Must, 32 Should, 11 Could.
