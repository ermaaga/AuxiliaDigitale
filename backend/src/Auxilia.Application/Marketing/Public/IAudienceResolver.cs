using Auxilia.Application.Abstractions.Marketing;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Marketing.Public;

/// <summary>The clients of a segment or a static list now (campaign recipients, M-03), for every client of the tenant.</summary>
public interface IAudienceResolver
{
    Task<Result<IReadOnlyList<Guid>>> SegmentAsync(Guid segmentId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<Guid>>> ListAsync(Guid listId, CancellationToken cancellationToken);
}

internal sealed class AudienceResolver(IAudienceDataFactory data, ITenantContext tenant, TimeProvider clock) : IAudienceResolver
{
    public async Task<Result<IReadOnlyList<Guid>>> SegmentAsync(Guid segmentId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindSegmentAsync(segmentId, cancellationToken) is not { } segment)
        {
            return Errors.Marketing.SegmentNotFound();
        }

        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return Result.Success(await store.SegmentMemberIdsAsync(SegmentRules.Deserialize(segment.Rule), AudienceScope.Everyone, today, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<Guid>>> ListAsync(Guid listId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindListAsync(listId, cancellationToken) is null
            ? Errors.Marketing.ListNotFound()
            : Result.Success(await store.ListMemberIdsAsync(listId, AudienceScope.Everyone, cancellationToken));
    }
}
