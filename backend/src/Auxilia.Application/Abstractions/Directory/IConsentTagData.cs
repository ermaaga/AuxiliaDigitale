using Auxilia.Domain.Directory;

namespace Auxilia.Application.Abstractions.Directory;

/// <summary>A tag with the number of clients (not deleted) that have it.</summary>
public sealed record TagRow(Guid Id, string Name, string? Color, int ClientCount);

/// <summary>A consent change with the name of who recorded it.</summary>
public sealed record ConsentRow(
    Guid Id, ConsentPurpose Purpose, ConsentChannel Channel, bool Granted, ConsentSource Source, string? Version, string? Note, DateTimeOffset RecordedAt,
    Guid? RecordedByUserId, string? RecordedByName);

/// <summary>What a marketing message needs about a client: e-mail, language and the current e-mail marketing consent.</summary>
public sealed record MarketingContactRow(Guid PersonId, string FirstName, string LastName, string? Email, string? Language, bool EmailMarketingConsent);

/// <summary>Tags and consents of the people of the current tenant (N01, M-01), one unit of work.</summary>
public interface IConsentTagData : IAsyncDisposable
{
    Task<IReadOnlyList<TagRow>> TagsAsync(CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<Tag?> FindTagAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> TagNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>The existing tags among <paramref name="ids"/>.</summary>
    Task<IReadOnlyList<Tag>> FindTagsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Name → id of the tags (case-insensitive).</summary>
    Task<IReadOnlyDictionary<string, Guid>> TagIdsByNameAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);

    /// <summary>The tags of a person, by name.</summary>
    Task<IReadOnlyList<Tag>> TagsOfAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>Tracked assignments of the people among <paramref name="personIds"/>.</summary>
    Task<IReadOnlyList<PersonTag>> AssignmentsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);

    /// <summary>The clients (not deleted) among <paramref name="ids"/>.</summary>
    Task<IReadOnlyList<Guid>> ExistingClientsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The consent changes of a person, newest first.</summary>
    Task<IReadOnlyList<ConsentRow>> ConsentsAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>The clients (not deleted) among <paramref name="personIds"/> with their e-mail marketing consent now.</summary>
    Task<IReadOnlyList<MarketingContactRow>> MarketingContactsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);

    /// <summary>Removes every assignment of a tag (before deleting it).</summary>
    Task RemoveAssignmentsAsync(Guid tagId, CancellationToken cancellationToken);

    void Add(Tag tag);

    void Remove(Tag tag);

    void Add(PersonTag assignment);

    void Remove(PersonTag assignment);

    void Add(Consent consent);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IConsentTagDataFactory
{
    Task<IConsentTagData> OpenAsync(CancellationToken cancellationToken);
}
