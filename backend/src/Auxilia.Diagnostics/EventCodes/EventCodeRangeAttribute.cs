namespace Auxilia.Diagnostics;

/// <summary>
/// Declares the block of 1000 event codes owned by a nested class of <see cref="EventCodes"/>.
/// Every constant of that class must lie in <c>[Start, Start + 999]</c> (verified by architecture tests).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventCodeRangeAttribute : Attribute
{
    public const int Size = 1000;

    public EventCodeRangeAttribute(int start, string owner)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 10000);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        Start = start;
        Owner = owner;
    }

    public int Start { get; }

    public int End => Start + Size - 1;

    /// <summary>The module or technical area that owns the range, as listed in the registry.</summary>
    public string Owner { get; }
}
