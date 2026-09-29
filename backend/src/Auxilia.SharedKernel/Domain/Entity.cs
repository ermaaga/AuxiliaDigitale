namespace Auxilia.SharedKernel.Domain;

/// <summary>Object with identity: two entities are equal when they have the same concrete type and id.</summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    /// <summary>For ORM materialization only.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; protected init; }

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);

    public bool Equals(Entity<TId>? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id)));

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
