using MCPServer.Domain.Exceptions;

namespace MCPServer.Domain.Abstractions;

/// <summary>
/// Generic aggregate / entity base providing a typed identifier, audit timestamps,
/// business-rule checking and a domain-events collection.
/// </summary>
public abstract class Entity<TId>
{
    public TId Id { get; protected set; } = default!;
    public DateTime CreatedAt { get; protected set; }
    public DateTime? ModifiedAt { get; protected set; }
    public string? ModifierId { get; protected set; }
    public string? CreatorId { get; protected set; }
    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }
    public string? EraserId { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() =>
        _domainEvents.Clear();

    protected static void CheckRule(IBusinessRule rule)
    {
        if (rule.IsBroken())
            throw rule.GetRelatedException();
    }

    protected async Task CheckRuleAsync(IBusinessRule rule, CancellationToken cancellationToken = default)
    {
        if (await rule.IsBrokenAsync(cancellationToken))
            throw rule.GetRelatedException();
    }
}

/// <summary>
/// Convenience non-generic alias; defaults to a <see cref="Guid"/> primary key —
/// matches the <c>User</c> aggregate which uses <c>public new Guid Id</c>.
/// </summary>
public abstract class Entity : Entity<Guid>;