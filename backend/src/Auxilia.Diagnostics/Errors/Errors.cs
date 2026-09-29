namespace Auxilia.Diagnostics;

/// <summary>
/// Factories of the expected failures returned as <c>Result.Failure</c> (ADR 0012), one nested class per
/// <see cref="EventCodes"/> range. Each factory uses its own event code; <c>Description</c> is technical English,
/// user-facing text is resolved by the client from the code.
/// </summary>
public static partial class Errors;
