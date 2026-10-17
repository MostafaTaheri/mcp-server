namespace MCPServer.Infrastructure.ExternalServices.Elastic;

/// <summary>
/// Physical field names for OTLP logs in the EDOT / OTel-native layouts, with fallbacks for dotted and nested variants.
/// Fixed for now; OBS-OTEL-02 replaces this with configurable mapping profiles.
/// </summary>
internal static class EdotLogFields
{
    public const string Timestamp = "@timestamp";

    public static readonly string[] Service = ["service.name", "resource.attributes.service.name"];
    public static readonly string[] Environment = ["service.environment", "resource.attributes.deployment.environment", "deployment.environment"];
    public static readonly string[] Severity = ["severity_text", "log.level"];
    public static readonly string[] TraceId = ["trace_id", "trace.id"];
    public static readonly string[] SpanId = ["span_id", "span.id"];
    public static readonly string[] Host = ["host.name", "resource.attributes.host.name"];
    public static readonly string[] Pod = ["k8s.pod.name", "resource.attributes.k8s.pod.name"];
    public static readonly string[] Body = ["body.text", "message", "body"];

    public static readonly string[] SourceFields =
        [Timestamp, .. Service, .. Environment, .. Severity, .. TraceId, .. SpanId, .. Host, .. Pod, .. Body];
}
