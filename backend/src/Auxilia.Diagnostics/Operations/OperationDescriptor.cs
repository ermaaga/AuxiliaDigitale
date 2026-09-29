namespace Auxilia.Diagnostics;

/// <summary>
/// A Manager operation run by <c>IOperationRunner</c> (ADR 0004): its unique <see cref="Name"/> (<c>&lt;Range&gt;.&lt;Field&gt;</c>,
/// used for traces, log scopes and metrics), the event code of its success log and whether it changes state
/// (writes run in a transaction with the outbox).
/// </summary>
public sealed record OperationDescriptor
{
    public OperationDescriptor(string name, int successCode, bool isWrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(successCode);

        Name = name;
        SuccessCode = successCode;
        IsWrite = isWrite;
    }

    public string Name { get; }

    public int SuccessCode { get; }

    public bool IsWrite { get; }

    public override string ToString() => Name;
}
