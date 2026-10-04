using Auxilia.Contracts.Engagement;

namespace Auxilia.Application.Abstractions.Timeline;

/// <summary>
/// A module's events in the timeline of a client (B-26), registered by the module: it shows only what the caller may
/// see (F10 for cases), newest first, at most <paramref name="take"/> entries before <paramref name="before"/>.
/// </summary>
public interface ITimelineContributor
{
    Task<IReadOnlyList<TimelineEntryResponse>> EntriesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken);
}
