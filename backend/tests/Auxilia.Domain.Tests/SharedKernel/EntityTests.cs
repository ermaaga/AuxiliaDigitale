using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Identifiers;

namespace Auxilia.Domain.Tests.SharedKernel;

public sealed class EntityTests
{
    [Fact]
    public void Equals_SameTypeAndId_AreEqual()
    {
        var id = IdGenerator.New();

        (new SampleEntity(id) == new SampleEntity(id)).ShouldBeTrue();
        new SampleEntity(id).GetHashCode().ShouldBe(new SampleEntity(id).GetHashCode());
    }

    [Fact]
    public void Equals_DifferentIds_AreNotEqual()
    {
        (new SampleEntity(IdGenerator.New()) != new SampleEntity(IdGenerator.New())).ShouldBeTrue();
    }

    [Fact]
    public void Equals_SameIdDifferentTypes_AreNotEqual()
    {
        var id = IdGenerator.New();

        new SampleEntity(id).Equals(new OtherEntity(id)).ShouldBeFalse();
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        new SampleEntity(IdGenerator.New()).Equals(null).ShouldBeFalse();
    }

    private sealed class SampleEntity(Guid id) : Entity<Guid>(id);

    private sealed class OtherEntity(Guid id) : Entity<Guid>(id);
}
