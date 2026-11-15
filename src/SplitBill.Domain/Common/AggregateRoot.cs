namespace SplitBill.Domain.Common;

/// <summary>Something that happened in the domain and that other parts of the system may react to.</summary>
public interface IDomainEvent
{
    /// <summary>Identifier of the aggregate that raised the event.</summary>
    Guid AggregateId { get; }

    /// <summary>When the event happened (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Base class for aggregate roots: collects domain events raised during an operation so the
/// application layer can write them to the transactional outbox in the same transaction.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Events raised since the last <see cref="ClearDomainEvents"/>.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Forgets the collected events; called after they have been persisted to the outbox.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Records an event to be published after the aggregate is saved.</summary>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
