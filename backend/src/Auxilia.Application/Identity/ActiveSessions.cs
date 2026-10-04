using Auxilia.Application.Abstractions.Identity;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>The open sessions of the tenant for the Administrators (F17, legacy <c>ActiveSessions</c> with real data, Q36).</summary>
public interface IActiveSessionQueryService
{
    Task<Result<PagedResponse<ActiveSessionResponse>>> ListAsync(ActiveSessionQuery query, Guid? currentSessionId, CancellationToken cancellationToken);

    Task<ActiveSessionSummaryResponse> SummaryAsync(CancellationToken cancellationToken);
}

internal sealed class ActiveSessionQueryService(IActiveSessionReader sessions, TimeProvider clock) : IActiveSessionQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxFilterLength = 200;

    /// <summary>"Active now" in the cards: used in the last minutes (legacy: last activity).</summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<string, ActiveSessionSort> Sorts = new(StringComparer.Ordinal)
    {
        ["lastUsedAt"] = ActiveSessionSort.LastUsedAt,
        ["createdAt"] = ActiveSessionSort.CreatedAt,
        ["userName"] = ActiveSessionSort.UserName,
    };

    public async Task<Result<PagedResponse<ActiveSessionResponse>>> ListAsync(ActiveSessionQuery query, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        var sortField = query.Sort?.TrimStart('-');
        var sort = ActiveSessionSort.LastUsedAt;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        if (query.UserName is { Length: > MaxFilterLength })
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        // The most recently used first unless a sort is chosen.
        var descending = string.IsNullOrEmpty(query.Sort) || query.Sort.StartsWith('-');
        var filter = new ActiveSessionFilter(
            string.IsNullOrWhiteSpace(query.UserName) ? null : query.UserName.Trim(),
            sort,
            descending,
            (query.Page - 1) * query.PageSize,
            query.PageSize,
            clock.GetUtcNow());
        var (items, total) = await sessions.PageAsync(filter, cancellationToken);
        return new PagedResponse<ActiveSessionResponse>(
            items.Select(row => new ActiveSessionResponse(
                row.Id,
                row.UserId,
                row.UserName,
                row.FullName,
                row.Roles.Select(role => role.ToString()).Order(StringComparer.Ordinal).ToArray(),
                row.ClientId,
                row.IpAddress,
                row.UserAgent,
                row.CreatedAt,
                row.LastUsedAt,
                row.ExpiresAt,
                row.Id == currentSessionId)).ToArray(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<ActiveSessionSummaryResponse> SummaryAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var counts = await sessions.CountAsync(now, now - ActiveWindow, cancellationToken);
        return new ActiveSessionSummaryResponse(counts.Sessions, counts.Users, counts.ActiveNow);
    }
}
