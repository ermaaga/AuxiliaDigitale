namespace Auxilia.Contracts.Messages.V1.Platform;

/// <summary>Runs a recurring job for the tenant of the message (manual run requested by the System, D-15).</summary>
public sealed record RunRecurringJobCommand(string JobCode) : ITenantMessage;
