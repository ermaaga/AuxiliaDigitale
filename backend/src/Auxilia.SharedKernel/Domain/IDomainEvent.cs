namespace Auxilia.SharedKernel.Domain;

/// <summary>Something that happened in the domain; dispatched after the transaction commits (through the outbox).</summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}
