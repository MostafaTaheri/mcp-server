namespace MCPServer.Domain.Abstractions;

public class BaseDomainEvent : IDomainEvent
{
    protected BaseDomainEvent()
    {
        MessageId = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Unique identifier for this message instance.
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// Correlation ID for tracing related messages across services.
    /// </summary>
    public string? CorrelationId { get; init;}

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; }
}