namespace Auxilia.Diagnostics;

/// <summary>
/// Catalog of the stable event codes <c>AUX-NNNNN</c> (ADR 0006, skill <c>auxilia-log-codes</c>).
/// One nested class per range; a new code is the current max of its range + 1. Codes are never reused,
/// renumbered or deleted: a retired code stays here marked <c>[Obsolete("Retired in &lt;version&gt;: &lt;reason&gt;")]</c>.
/// The same code is the <c>EventId</c> of the <see cref="Log"/> entry and the <c>Code</c> of the <see cref="Errors"/> factory.
/// </summary>
public static partial class EventCodes;
