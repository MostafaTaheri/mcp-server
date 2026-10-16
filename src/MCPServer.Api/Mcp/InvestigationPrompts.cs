using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPServer.Api.Mcp;

[McpServerPromptType]
public sealed class InvestigationPrompts
{
    private const string Preamble =
        "Work read-only and from evidence. First call the tool obs_get_connection_status to confirm Elastic is reachable. " +
        "Read the resources obs://services (to confirm the exact service name) and obs://fields/logs (to use real field names) before querying. " +
        "Start with a narrow scope and widen only if needed. ";

    private const string Evidence =
        " Cite evidence for every claim: trace IDs, log document IDs with timestamps, and Kibana links where available. " +
        "State what you could not verify, and finish with a short summary: likely cause, confidence, and the next step.";

    [McpServerPrompt(Name = "investigate_error_spike")]
    [Description("Guides the assistant through investigating a spike in errors for a service.")]
    public string InvestigateErrorSpike(
        [Description("Exact service.name")] string service,
        [Description("Time window to examine, e.g. 15m, 1h")] string timeWindow = "15m")
        => Preamble +
           $"Investigate an error spike in service '{service}' over the last {timeWindow}. " +
           "1) Compare the error count with the preceding window of equal length to confirm the spike. " +
           "2) Group errors by message, exception type and endpoint to find the dominant pattern. " +
           "3) Pick representative failing traces and follow them to the first failing span or downstream dependency. " +
           "4) Check whether the errors started at a specific time, version or host." + Evidence;

    [McpServerPrompt(Name = "why_is_trace_slow")]
    [Description("Guides the assistant through explaining why a specific trace was slow.")]
    public string WhyIsTraceSlow(
        [Description("Trace ID to analyse")] string traceId)
        => Preamble +
           $"Explain why trace '{traceId}' was slow. " +
           "1) Load all spans of the trace and the total duration. " +
           "2) Find the critical path and the spans with the largest self time (not just the largest duration). " +
           "3) Look for sequential calls that could be parallel, repeated calls (N+1), retries and timeouts. " +
           "4) Correlate with logs that carry the same trace ID around the slow spans." + Evidence;

    [McpServerPrompt(Name = "compare_before_after_deploy")]
    [Description("Guides the assistant through comparing a service's behaviour before and after a deploy.")]
    public string CompareBeforeAfterDeploy(
        [Description("Exact service.name")] string service,
        [Description("When the deploy happened, ISO 8601 UTC")] string deployTime,
        [Description("Length of each comparison window, e.g. 30m, 2h")] string timeWindow = "30m")
        => Preamble +
           $"Compare service '{service}' for {timeWindow} before and {timeWindow} after the deploy at {deployTime}. " +
           "1) Compare error rate, latency percentiles (p50/p95/p99) and request volume across the two windows. " +
           "2) Check the service version or build field to confirm the deploy took effect. " +
           "3) Identify new error messages that did not exist before, and endpoints whose latency changed most. " +
           "4) Say whether the deploy is a likely cause, or whether the change was already underway." + Evidence;
}
