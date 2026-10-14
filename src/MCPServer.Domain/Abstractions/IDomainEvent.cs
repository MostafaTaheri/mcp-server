namespace MCPServer.Domain.Abstractions;

/// <summary>
/// Marker interface for all domain events.
/// </summary>
public interface IDomainEvent
{

    /// <summary>
    /// Unique identifier for this message instance.
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// Correlation ID for tracing related messages across services.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; }
}
