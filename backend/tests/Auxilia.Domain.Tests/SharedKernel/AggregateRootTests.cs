using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Identifiers;

namespace Auxilia.Domain.Tests.SharedKernel;

public sealed class AggregateRootTests
{
    [Fact]
    public void Raise_AddsDomainEvent()
    {
        var aggregate = new SampleAggregate(IdGenerator.New());

        aggregate.DoSomething();

        aggregate.DomainEvents.Single().ShouldBeOfType<SomethingHappened>();
    }

    [Fact]
    public void DequeueDomainEvents_ReturnsEventsAndClears()
    {
        var aggregate = new SampleAggregate(IdGenerator.New());
        aggregate.DoSomething();
        aggregate.DoSomething();

        var events = aggregate.DequeueDomainEvents();

        events.Count.ShouldBe(2);
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    private sealed record SomethingHappened(Guid EventId, DateTimeOffset OccurredAt, Guid AggregateId) : IDomainEvent;

    private sealed class SampleAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void DoSomething() => Raise(new SomethingHappened(IdGenerator.New(), DateTimeOffset.UtcNow, Id));
    }
}
