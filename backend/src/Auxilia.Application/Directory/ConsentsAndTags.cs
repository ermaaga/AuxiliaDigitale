using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Directory;

/// <summary>Tags of the clients (N01, M-01): Administrators create, rename and delete them; staff put them on clients.</summary>
public interface ITagManager
{
    Task<Result<Guid>> CreateAsync(SaveTagRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, SaveTagRequest request, CancellationToken cancellationToken);

    /// <summary>Removes it from every client too.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Replaces the tags of a client.</summary>
    Task<Result> SetClientTagsAsync(Guid clientId, IReadOnlyList<Guid>? tagIds, CancellationToken cancellationToken);

    /// <returns>Assignments added plus removed.</returns>
    Task<Result<int>> BulkAsync(BulkClientTagsRequest request, CancellationToken cancellationToken);
}

public interface ITagQueryService
{
    Task<IReadOnlyList<TagResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ClientTagResponse>>> ClientTagsAsync(Guid clientId, CancellationToken cancellationToken);
}

/// <summary>Consents of the clients (N01): an append-only history per purpose and channel.</summary>
public interface IConsentManager
{
    /// <summary>A change recorded by staff (source <c>Staff</c>).</summary>
    Task<Result> RecordAsync(Guid clientId, RecordConsentRequest request, CancellationToken cancellationToken);
}

public interface IConsentQueryService
{
    Task<Result<ClientConsentsResponse>> GetAsync(Guid clientId, CancellationToken cancellationToken);
}

internal sealed class TagManager(IOperationRunner operations, IConsentTagDataFactory data, ICurrentUser currentUser, TimeProvider clock) : ITagManager
{
    public const int MaxBulkClients = 500;

    public Task<Result<Guid>> CreateAsync(SaveTagRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Directory.CreateTag, null, async scope =>
        {
            var created = Tag.Create(Guid.CreateVersion7(), request.Name, request.Color);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.TagNameTakenAsync(created.Value.Name, null, cancellationToken))
            {
                return Errors.Directory.TagInvalid("name", "validation.tags.nameTaken");
            }

            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Tag), created.Value.Id);
            return created.Value.Id;
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, SaveTagRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Directory.UpdateTag, new { TagId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTagAsync(id, cancellationToken) is not { } tag)
            {
                return Errors.Directory.TagNotFound();
            }

            var updated = tag.Update(request.Name, request.Color);
            if (updated.IsFailure)
            {
                return updated;
            }

            if (await store.TagNameTakenAsync(tag.Name, id, cancellationToken))
            {
                return Errors.Directory.TagInvalid("name", "validation.tags.nameTaken");
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.DeleteTag, new { TagId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTagAsync(id, cancellationToken) is not { } tag)
            {
                return Errors.Directory.TagNotFound();
            }

            await store.RemoveAssignmentsAsync(id, cancellationToken);
            store.Remove(tag);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SetClientTagsAsync(Guid clientId, IReadOnlyList<Guid>? tagIds, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeClientTags, new { ClientId = clientId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if ((await store.ExistingClientsAsync([clientId], cancellationToken)).Count == 0)
            {
                return Errors.Directory.ClientNotFound();
            }

            var wanted = (tagIds ?? []).Distinct().ToArray();
            if ((await store.FindTagsAsync(wanted, cancellationToken)).Count != wanted.Length)
            {
                return Errors.Directory.TagInvalid("tagIds", "validation.tags.unknown");
            }

            var current = await store.AssignmentsAsync([clientId], cancellationToken);
            foreach (var removed in current.Where(assignment => !wanted.Contains(assignment.TagId)))
            {
                store.Remove(removed);
            }

            foreach (var added in wanted.Where(tagId => current.All(assignment => assignment.TagId != tagId)))
            {
                store.Add(new PersonTag(clientId, added, currentUser.UserId, clock.GetUtcNow()));
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<int>> BulkAsync(BulkClientTagsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<int>(Operations.Directory.ChangeClientTags, new { Clients = request.ClientIds?.Count }, async _ =>
        {
            var clients = (request.ClientIds ?? []).Distinct().ToArray();
            var add = (request.Add ?? []).Distinct().ToArray();
            var remove = (request.Remove ?? []).Distinct().Where(id => !add.Contains(id)).ToArray();
            if (clients.Length is 0 or > MaxBulkClients)
            {
                return Errors.Directory.TagInvalid("clientIds", "validation.tags.clients");
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var tags = add.Concat(remove).ToArray();
            if ((await store.FindTagsAsync(tags, cancellationToken)).Count != tags.Length)
            {
                return Errors.Directory.TagInvalid("tags", "validation.tags.unknown");
            }

            var existing = (await store.ExistingClientsAsync(clients, cancellationToken)).ToHashSet();
            var assignments = await store.AssignmentsAsync([.. existing], cancellationToken);
            var changed = 0;
            var now = clock.GetUtcNow();
            foreach (var clientId in existing)
            {
                foreach (var tagId in add.Where(tagId => !assignments.Any(item => item.PersonId == clientId && item.TagId == tagId)))
                {
                    store.Add(new PersonTag(clientId, tagId, currentUser.UserId, now));
                    changed++;
                }
            }

            foreach (var removed in assignments.Where(item => remove.Contains(item.TagId)))
            {
                store.Remove(removed);
                changed++;
            }

            await store.SaveChangesAsync(cancellationToken);
            return changed;
        }, cancellationToken);
    }
}

internal sealed class TagQueryService(IConsentTagDataFactory data) : ITagQueryService
{
    public async Task<IReadOnlyList<TagResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.TagsAsync(cancellationToken)).Select(tag => new TagResponse(tag.Id, tag.Name, tag.Color, tag.ClientCount))];
    }

    public async Task<Result<IReadOnlyList<ClientTagResponse>>> ClientTagsAsync(Guid clientId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if ((await store.ExistingClientsAsync([clientId], cancellationToken)).Count == 0)
        {
            return Errors.Directory.ClientNotFound();
        }

        return (await store.TagsOfAsync(clientId, cancellationToken)).Select(tag => new ClientTagResponse(tag.Id, tag.Name, tag.Color)).ToArray();
    }
}

internal sealed class ConsentManager(IOperationRunner operations, IConsentTagDataFactory data, ICurrentUser currentUser, TimeProvider clock) : IConsentManager
{
    public Task<Result> RecordAsync(Guid clientId, RecordConsentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var purpose = Enum.TryParse<ConsentPurpose>(request.Purpose, ignoreCase: true, out var parsedPurpose) ? parsedPurpose : (ConsentPurpose)(-1);
        var channel = Enum.TryParse<ConsentChannel>(request.Channel, ignoreCase: true, out var parsedChannel) ? parsedChannel : (ConsentChannel)(-1);
        return RecordAsync(clientId, purpose, channel, request.Granted, ConsentSource.Staff, request.Version, request.Note, cancellationToken);
    }

    /// <summary>A change from any source (imports use <see cref="ConsentSource.Import"/>).</summary>
    internal Task<Result> RecordAsync(
        Guid clientId, ConsentPurpose purpose, ConsentChannel channel, bool granted, ConsentSource source, string? version, string? note,
        CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.RecordConsent, new { ClientId = clientId, Purpose = purpose, Channel = channel, Granted = granted, Source = source }, async _ =>
        {
            var recorded = Consent.Record(Guid.CreateVersion7(), clientId, purpose, channel, granted, source, version, note, currentUser.UserId, clock.GetUtcNow());
            if (recorded.IsFailure)
            {
                return Result.Failure(recorded.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if ((await store.ExistingClientsAsync([clientId], cancellationToken)).Count == 0)
            {
                return Errors.Directory.ClientNotFound();
            }

            store.Add(recorded.Value);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
}

internal sealed class ConsentQueryService(IConsentTagDataFactory data) : IConsentQueryService
{
    public async Task<Result<ClientConsentsResponse>> GetAsync(Guid clientId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if ((await store.ExistingClientsAsync([clientId], cancellationToken)).Count == 0)
        {
            return Errors.Directory.ClientNotFound();
        }

        var history = await store.ConsentsAsync(clientId, cancellationToken);
        var current = (from purpose in Enum.GetValues<ConsentPurpose>()
                       from channel in Enum.GetValues<ConsentChannel>()
                       let latest = history.FirstOrDefault(change => change.Purpose == purpose && change.Channel == channel)
                       select new ConsentStateResponse(
                           purpose.ToString(), channel.ToString(), latest?.Granted ?? false, latest?.RecordedAt, latest?.Source.ToString())).ToArray();
        return new ClientConsentsResponse(
            current,
            [.. history.Select(change => new ConsentChangeResponse(
                change.Id, change.Purpose.ToString(), change.Channel.ToString(), change.Granted, change.Source.ToString(), change.Version, change.Note,
                change.RecordedAt, change.RecordedByName))]);
    }
}
