namespace Auxilia.Diagnostics;

/// <summary>
/// Catalog of Manager operations, one nested class per <see cref="EventCodes"/> range, e.g.
/// <c>public static readonly OperationDescriptor Create = new("Cases.Create", EventCodes.Cases.CaseCreated);</c>.
/// Names are unique and match <c>&lt;Range&gt;.&lt;Field&gt;</c>; the success code belongs to the same range and to one
/// operation only (verified by architecture tests).
/// </summary>
public static partial class Operations;
