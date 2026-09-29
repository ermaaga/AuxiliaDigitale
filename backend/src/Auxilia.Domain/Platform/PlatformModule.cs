using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Platform;

public enum ModuleKind
{
    /// <summary>Always visible (still subject to role permissions).</summary>
    Core,

    Optional,
}

/// <summary>
/// A pluggable module (catalog <c>modules</c>), synchronised from the module descriptors (task P1-11).
/// The key is the module code (e.g. <c>cases</c>).
/// </summary>
public sealed class PlatformModule : Entity<string>
{
    public const int CodeMaxLength = 50;
    public const int NameKeyMaxLength = 150;

    public PlatformModule(string code, ModuleKind kind, string nameKey, int eventCodeRangeStart)
        : base(code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code.Length, CodeMaxLength);

        Kind = kind;
        EventCodeRangeStart = eventCodeRangeStart;
        NameKey = nameKey;
        IsAvailable = true;
    }

    private PlatformModule()
    {
        NameKey = string.Empty;
    }

    public ModuleKind Kind { get; private set; }

    /// <summary>Translation key of the module name.</summary>
    public string NameKey { get; private set; }

    public int EventCodeRangeStart { get; private set; }

    /// <summary>False when the module is no longer shipped by the code (kept for history, never deleted).</summary>
    public bool IsAvailable { get; private set; }

    public void Update(ModuleKind kind, string nameKey, int eventCodeRangeStart)
    {
        Kind = kind;
        NameKey = nameKey;
        EventCodeRangeStart = eventCodeRangeStart;
        IsAvailable = true;
    }

    public void MarkUnavailable() => IsAvailable = false;
}
