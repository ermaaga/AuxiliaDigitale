using System.Text.Json;

namespace Auxilia.Contracts.Marketing;

/// <summary>
/// A condition of a segment rule (N01). <c>field</c>: <c>status</c>, <c>employee</c>, <c>tag</c>, <c>specialization</c>,
/// <c>service</c>, <c>caseStatus</c>, <c>age</c>, <c>createdOn</c>, <c>customField</c> (with <c>key</c>); <c>op</c>: see
/// <c>GET /marketing/segments/fields</c>; <c>value</c>: a status, an id, a number, a date <c>yyyy-MM-dd</c> or, for custom
/// fields, a JSON string/number/boolean.
/// </summary>
public sealed record SegmentConditionRequest(string Field, string Op, JsonElement? Value, string? Key);

/// <param name="Match"><c>all</c> (AND) or <c>any</c> (OR).</param>
public sealed record SegmentGroupRequest(string Match, IReadOnlyList<SegmentConditionRequest> Conditions);

/// <summary>A segment rule: conditions and groups of conditions joined by <c>match</c> (<c>all</c> / <c>any</c>).</summary>
public sealed record SegmentRuleRequest(string Match, IReadOnlyList<SegmentConditionRequest> Conditions, IReadOnlyList<SegmentGroupRequest>? Groups);

public sealed record SaveSegmentRequest(string Name, string? Description, SegmentRuleRequest Rule);

public sealed record CreatedAudienceResponse(Guid Id);

public sealed record SegmentListItemResponse(Guid Id, string Name, string? Description, DateTimeOffset UpdatedAt);

/// <param name="MemberCount">The clients it selects now (an employee counts the clients in their charge).</param>
public sealed record SegmentResponse(Guid Id, string Name, string? Description, SegmentRuleRequest Rule, int MemberCount, DateTimeOffset UpdatedAt);

/// <summary>A field of the segment builder with the operators it accepts.</summary>
public sealed record SegmentFieldResponse(string Field, IReadOnlyList<string> Operators);

public sealed record AudienceMemberResponse(Guid Id, string FullName, string? Email, string Status);

/// <summary>What a rule selects now: the count and the first clients by name.</summary>
public sealed record SegmentPreviewResponse(int Count, IReadOnlyList<AudienceMemberResponse> Sample);

public sealed record SaveStaticListRequest(string Name, string? Description);

public sealed record StaticListResponse(Guid Id, string Name, string? Description, int MemberCount, DateTimeOffset UpdatedAt);

/// <summary>Clients to add to or remove from a static list (at most 500).</summary>
public sealed record ListMembersRequest(IReadOnlyList<Guid> ClientIds);

/// <param name="Changed">Clients added or removed.</param>
public sealed record ListMembersChangedResponse(int Changed);
