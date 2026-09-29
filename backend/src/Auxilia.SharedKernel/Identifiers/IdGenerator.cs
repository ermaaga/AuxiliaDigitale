namespace Auxilia.SharedKernel.Identifiers;

/// <summary>Creates ids for exposed entities: time-ordered Guid v7 (index friendly, safe to expose, offline/mobile friendly).</summary>
public static class IdGenerator
{
    public static Guid New() => Guid.CreateVersion7();

    /// <summary>Creates an id whose timestamp comes from <paramref name="timeProvider"/> (deterministic in tests).</summary>
    public static Guid New(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return Guid.CreateVersion7(timeProvider.GetUtcNow());
    }
}
