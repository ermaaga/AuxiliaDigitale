using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Marketing;
using Auxilia.Diagnostics;
using Auxilia.Domain.Marketing;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Marketing;

/// <summary>Dynamic segments (N01, M-02): a name and a validated rule, counted live.</summary>
public interface ISegmentManager
{
    Task<Result<Guid>> CreateAsync(SaveSegmentRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, SaveSegmentRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ISegmentQueryService
{
    IReadOnlyList<SegmentFieldResponse> Fields();

    Task<IReadOnlyList<SegmentListItemResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Result<SegmentResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The count and the first clients a rule selects (before saving it).</summary>
    Task<Result<SegmentPreviewResponse>> PreviewAsync(SegmentRuleRequest rule, CancellationToken cancellationToken);

    Task<Result<PagedResponse<AudienceMemberResponse>>> MembersAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>Static lists (N01, M-02): clients chosen by hand, from a selection of the clients table or imported.</summary>
public interface IStaticListManager
{
    Task<Result<Guid>> CreateAsync(SaveStaticListRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, SaveStaticListRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <returns>Clients added (those already in the list or outside the caller's clients are skipped).</returns>
    Task<Result<int>> AddMembersAsync(Guid id, IReadOnlyList<Guid>? clientIds, CancellationToken cancellationToken);

    Task<Result<int>> RemoveMembersAsync(Guid id, IReadOnlyList<Guid>? clientIds, CancellationToken cancellationToken);
}

public interface IStaticListQueryService
{
    Task<IReadOnlyList<StaticListResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Result<StaticListResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<PagedResponse<AudienceMemberResponse>>> MembersAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// Whose clients an audience contains for the caller (N01: tenant isolation and the employees' visibility):
/// Administrators every client, employees the clients in their charge; the System (imports, campaigns) every client.
/// </summary>
internal sealed class AudiencePolicy(ICurrentUser currentUser)
{
    public AudienceScope Scope =>
        currentUser.ActorType == ActorType.User && !currentUser.Roles.Contains(TenantRole.Administrator)
            ? new AudienceScope(currentUser.UserId ?? Guid.Empty)
            : AudienceScope.Everyone;
}

/// <summary>Segment rules between the API shape and the domain, and as stored JSON.</summary>
internal static class SegmentRules
{
    public const int MaxListMembers = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Result<SegmentRule> ToDomain(SegmentRuleRequest? request)
    {
        if (request is null)
        {
            return Errors.Marketing.SegmentInvalid("rule", "validation.segments.empty");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var conditions = Conditions(request.Conditions, "rule.conditions", errors);
        var groups = (request.Groups ?? []).Select((group, index) =>
            new SegmentGroup(Match(group.Match, $"rule.groups[{index}].match", errors), Conditions(group.Conditions, $"rule.groups[{index}].conditions", errors))).ToArray();
        var rule = new SegmentRule(Match(request.Match, "rule.match", errors), conditions, groups);
        if (errors.Count > 0)
        {
            return Errors.Marketing.SegmentInvalid(errors);
        }

        var valid = rule.Validate();
        return valid.IsFailure ? Result.Failure<SegmentRule>(valid.Error!) : rule;
    }

    public static string Serialize(SegmentRule rule) => JsonSerializer.Serialize(rule, Json);

    public static SegmentRule Deserialize(string json) => JsonSerializer.Deserialize<SegmentRule>(json, Json)!;

    public static SegmentRuleRequest ToRequest(SegmentRule rule) =>
        new(rule.MatchAll ? "all" : "any", [.. rule.Conditions.Select(ToRequest)], [.. rule.Groups.Select(group => new SegmentGroupRequest(group.MatchAll ? "all" : "any", [.. group.Conditions.Select(ToRequest)]))]);

    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static SegmentConditionRequest ToRequest(SegmentCondition condition) =>
        new(Name(condition.Field), Name(condition.Operator), condition.Value is null ? null : Value(condition), condition.Key);

    private static JsonElement Value(SegmentCondition condition) =>
        condition.Field is SegmentField.CustomField or SegmentField.Age
            ? JsonDocument.Parse(condition.Value!).RootElement.Clone()
            : JsonSerializer.SerializeToElement(condition.Value);

    private static bool Match(string? match, string path, Dictionary<string, string[]> errors)
    {
        switch (match?.ToUpperInvariant())
        {
            case "ALL":
                return true;
            case "ANY":
                return false;
            default:
                errors[path] = ["validation.segments.match"];
                return true;
        }
    }

    private static SegmentCondition[] Conditions(IReadOnlyList<SegmentConditionRequest>? conditions, string prefix, Dictionary<string, string[]> errors) =>
        [.. (conditions ?? []).Select((condition, index) =>
        {
            var path = $"{prefix}[{index}]";
            if (!Enum.TryParse<SegmentField>(condition.Field, ignoreCase: true, out var field) || !Enum.IsDefined(field))
            {
                errors[$"{path}.field"] = ["validation.segments.field"];
            }

            if (!Enum.TryParse<SegmentOperator>(condition.Op, ignoreCase: true, out var op) || !Enum.IsDefined(op))
            {
                errors[$"{path}.op"] = ["validation.segments.op"];
            }

            return new SegmentCondition(field, op, Canonical(field, condition.Value, path, errors), string.IsNullOrWhiteSpace(condition.Key) ? null : condition.Key.Trim());
        })];

    /// <summary>The value as canonical text: strings as they are, numbers invariant, custom field values as a JSON literal.</summary>
    private static string? Canonical(SegmentField field, JsonElement? value, string path, Dictionary<string, string[]> errors)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (field == SegmentField.CustomField)
        {
            if (element.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return element.GetRawText();
            }

            errors[$"{path}.value"] = ["validation.segments.value"];
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null,
        };
    }
}

internal sealed class SegmentManager(IOperationRunner operations, IAudienceDataFactory data) : ISegmentManager
{
    public Task<Result<Guid>> CreateAsync(SaveSegmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Marketing.CreateSegment, null, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var segment = new Segment(Guid.CreateVersion7());
            var applied = await ApplyAsync(store, segment, request, cancellationToken);
            if (applied.IsFailure)
            {
                return Result.Failure<Guid>(applied.Error!);
            }

            store.Add(segment);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Segment), segment.Id);
            return segment.Id;
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, SaveSegmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Marketing.UpdateSegment, new { SegmentId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindSegmentAsync(id, cancellationToken) is not { } segment)
            {
                return Errors.Marketing.SegmentNotFound();
            }

            var applied = await ApplyAsync(store, segment, request, cancellationToken);
            if (applied.IsFailure)
            {
                return applied;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.DeleteSegment, new { SegmentId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindSegmentAsync(id, cancellationToken) is not { } segment)
            {
                return Errors.Marketing.SegmentNotFound();
            }

            store.Remove(segment);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private static async Task<Result> ApplyAsync(IAudienceData store, Segment segment, SaveSegmentRequest request, CancellationToken cancellationToken)
    {
        var rule = SegmentRules.ToDomain(request.Rule);
        var updated = segment.Update(request.Name, request.Description, rule.IsSuccess ? SegmentRules.Serialize(rule.Value) : "{}");
        if (rule.IsFailure || updated.IsFailure)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var (key, value) in (rule.Error?.ValidationErrors ?? new Dictionary<string, string[]>()).Concat(updated.Error?.ValidationErrors ?? new Dictionary<string, string[]>()))
            {
                errors[key] = value;
            }

            return Errors.Marketing.SegmentInvalid(errors);
        }

        return await store.SegmentNameTakenAsync(segment.Name, segment.Id, cancellationToken)
            ? Errors.Marketing.SegmentInvalid("name", "validation.segments.nameTaken")
            : Result.Success();
    }
}

internal sealed class SegmentQueryService(IAudienceDataFactory data, AudiencePolicy policy, ITenantContext tenant, TimeProvider clock) : ISegmentQueryService
{
    public const int MaxPageSize = 100;
    public const int SampleSize = 10;

    public IReadOnlyList<SegmentFieldResponse> Fields() =>
        [.. SegmentRule.Operators.Select(pair => new SegmentFieldResponse(SegmentRules.Name(pair.Key), [.. pair.Value.Select(SegmentRules.Name)]))];

    public async Task<IReadOnlyList<SegmentListItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.SegmentsAsync(cancellationToken)).Select(row => new SegmentListItemResponse(row.Id, row.Name, row.Description, row.UpdatedAt))];
    }

    public async Task<Result<SegmentResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if ((await store.SegmentsAsync(cancellationToken)).FirstOrDefault(row => row.Id == id) is not { } row)
        {
            return Errors.Marketing.SegmentNotFound();
        }

        var rule = SegmentRules.Deserialize(row.Rule);
        var (_, total) = await store.SegmentMembersAsync(rule, policy.Scope, Today(), 0, 0, cancellationToken);
        return new SegmentResponse(row.Id, row.Name, row.Description, SegmentRules.ToRequest(rule), total, row.UpdatedAt);
    }

    public async Task<Result<SegmentPreviewResponse>> PreviewAsync(SegmentRuleRequest rule, CancellationToken cancellationToken)
    {
        var parsed = SegmentRules.ToDomain(rule);
        if (parsed.IsFailure)
        {
            return Result.Failure<SegmentPreviewResponse>(parsed.Error!);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.SegmentMembersAsync(parsed.Value, policy.Scope, Today(), 0, SampleSize, cancellationToken);
        return new SegmentPreviewResponse(total, [.. items.Select(Member)]);
    }

    public async Task<Result<PagedResponse<AudienceMemberResponse>>> MembersAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindSegmentAsync(id, cancellationToken) is not { } segment)
        {
            return Errors.Marketing.SegmentNotFound();
        }

        var (items, total) = await store.SegmentMembersAsync(SegmentRules.Deserialize(segment.Rule), policy.Scope, Today(), (page - 1) * pageSize, pageSize, cancellationToken);
        return new PagedResponse<AudienceMemberResponse>([.. items.Select(Member)], page, pageSize, total);
    }

    internal static AudienceMemberResponse Member(AudienceMemberRow row) => new(row.Id, $"{row.FirstName} {row.LastName}", row.Email, row.Status.ToString());

    private DateOnly Today()
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}

internal sealed class StaticListManager(IOperationRunner operations, IAudienceDataFactory data, AudiencePolicy policy, ICurrentUser currentUser, TimeProvider clock)
    : IStaticListManager
{
    public Task<Result<Guid>> CreateAsync(SaveStaticListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Marketing.CreateList, null, async scope =>
        {
            var list = new StaticList(Guid.CreateVersion7());
            var updated = list.Update(request.Name, request.Description);
            if (updated.IsFailure)
            {
                return Result.Failure<Guid>(updated.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.ListNameTakenAsync(list.Name, null, cancellationToken))
            {
                return Errors.Marketing.ListInvalid("name", "validation.lists.nameTaken");
            }

            store.Add(list);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(StaticList), list.Id);
            return list.Id;
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, SaveStaticListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Marketing.UpdateList, new { ListId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindListAsync(id, cancellationToken) is not { } list)
            {
                return Errors.Marketing.ListNotFound();
            }

            var updated = list.Update(request.Name, request.Description);
            if (updated.IsFailure)
            {
                return updated;
            }

            if (await store.ListNameTakenAsync(list.Name, id, cancellationToken))
            {
                return Errors.Marketing.ListInvalid("name", "validation.lists.nameTaken");
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Marketing.DeleteList, new { ListId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindListAsync(id, cancellationToken) is not { } list)
            {
                return Errors.Marketing.ListNotFound();
            }

            store.Remove(list);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<int>> AddMembersAsync(Guid id, IReadOnlyList<Guid>? clientIds, CancellationToken cancellationToken) =>
        ChangeAsync(id, clientIds, async (store, ids) =>
        {
            var inScope = await store.ClientsInScopeAsync(ids, policy.Scope, cancellationToken);
            var already = (await store.MembersAmongAsync(id, inScope, cancellationToken)).ToHashSet();
            var added = inScope.Where(client => !already.Contains(client)).ToArray();
            store.AddMembers(added.Select(client => new StaticListMember(id, client, currentUser.UserId, clock.GetUtcNow())));
            await store.SaveChangesAsync(cancellationToken);
            return added.Length;
        }, cancellationToken);

    public Task<Result<int>> RemoveMembersAsync(Guid id, IReadOnlyList<Guid>? clientIds, CancellationToken cancellationToken) =>
        ChangeAsync(id, clientIds, async (store, ids) =>
            await store.RemoveMembersAsync(id, await store.ClientsInScopeAsync(ids, policy.Scope, cancellationToken), cancellationToken), cancellationToken);

    private Task<Result<int>> ChangeAsync(Guid id, IReadOnlyList<Guid>? clientIds, Func<IAudienceData, Guid[], Task<int>> change, CancellationToken cancellationToken) =>
        operations.RunAsync<int>(Operations.Marketing.ChangeListMembers, new { ListId = id, Clients = clientIds?.Count }, async _ =>
        {
            var ids = (clientIds ?? []).Distinct().ToArray();
            if (ids.Length is 0 or > SegmentRules.MaxListMembers)
            {
                return Errors.Marketing.ListInvalid("clientIds", "validation.lists.clients");
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindListAsync(id, cancellationToken) is null)
            {
                return Errors.Marketing.ListNotFound();
            }

            return await change(store, ids);
        }, cancellationToken);
}

internal sealed class StaticListQueryService(IAudienceDataFactory data, AudiencePolicy policy) : IStaticListQueryService
{
    public async Task<IReadOnlyList<StaticListResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.ListsAsync(policy.Scope, cancellationToken)).Select(ToResponse)];
    }

    public async Task<Result<StaticListResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.ListsAsync(policy.Scope, cancellationToken)).FirstOrDefault(row => row.Id == id) is { } row
            ? ToResponse(row)
            : Errors.Marketing.ListNotFound();
    }

    public async Task<Result<PagedResponse<AudienceMemberResponse>>> MembersAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, SegmentQueryService.MaxPageSize));
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindListAsync(id, cancellationToken) is null)
        {
            return Errors.Marketing.ListNotFound();
        }

        var (items, total) = await store.ListMembersAsync(id, policy.Scope, (page - 1) * pageSize, pageSize, cancellationToken);
        return new PagedResponse<AudienceMemberResponse>([.. items.Select(SegmentQueryService.Member)], page, pageSize, total);
    }

    private static StaticListResponse ToResponse(StaticListRow row) => new(row.Id, row.Name, row.Description, row.MemberCount, row.UpdatedAt);
}
