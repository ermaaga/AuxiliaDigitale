using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Directory;

internal sealed class ConsentTagDataFactory(ITenantDbContextFactory databases) : IConsentTagDataFactory
{
    public async Task<IConsentTagData> OpenAsync(CancellationToken cancellationToken) => new ConsentTagData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IConsentTagData"/>
#pragma warning disable CA1304, CA1311 // ToUpper() is translated to SQL upper(): no culture involved.
internal sealed class ConsentTagData(ITenantDbContext db) : IConsentTagData
{
    public async Task<IReadOnlyList<TagRow>> TagsAsync(CancellationToken cancellationToken)
    {
        var assignments = db.Set<PersonTag>();
        var people = db.Set<Person>();
        return await db.Set<Tag>().AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagRow(
                tag.Id, tag.Name, tag.Color, assignments.Count(assignment => assignment.TagId == tag.Id && people.Any(person => person.Id == assignment.PersonId))))
            .ToListAsync(cancellationToken);
    }

    public Task<Tag?> FindTagAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Tag>().SingleOrDefaultAsync(tag => tag.Id == id, cancellationToken);

    public Task<bool> TagNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<Tag>().AnyAsync(tag => tag.Name == name && tag.Id != exceptId, cancellationToken);

    public async Task<IReadOnlyList<Tag>> FindTagsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Set<Tag>().Where(tag => ids.Contains(tag.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, Guid>> TagIdsByNameAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        var wanted = names.Select(name => name.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var found = await db.Set<Tag>().AsNoTracking().Where(tag => wanted.Contains(tag.Name.ToUpper())).Select(tag => new { tag.Name, tag.Id }).ToListAsync(cancellationToken);
        return found.ToDictionary(item => item.Name, item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<Tag>> TagsOfAsync(Guid personId, CancellationToken cancellationToken) =>
        await (from assignment in db.Set<PersonTag>().AsNoTracking()
               join tag in db.Set<Tag>() on assignment.TagId equals tag.Id
               where assignment.PersonId == personId
               orderby tag.Name
               select tag).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PersonTag>> AssignmentsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        await db.Set<PersonTag>().Where(assignment => personIds.Contains(assignment.PersonId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ExistingClientsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await (from person in db.Set<Person>().AsNoTracking()
               join profile in db.Set<ClientProfile>() on person.Id equals profile.Id
               where ids.Contains(person.Id)
               select person.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConsentRow>> ConsentsAsync(Guid personId, CancellationToken cancellationToken)
    {
        var users = db.Set<User>();
        var people = db.Set<Person>();
        return await db.Set<Consent>().AsNoTracking()
            .Where(consent => consent.PersonId == personId)
            .OrderByDescending(consent => consent.RecordedAt)
            .ThenByDescending(consent => consent.Id)
            .Select(consent => new ConsentRow(
                consent.Id, consent.Purpose, consent.Channel, consent.Granted, consent.Source, consent.Version, consent.Note, consent.RecordedAt,
                consent.RecordedByUserId,
                users.Where(user => user.Id == consent.RecordedByUserId)
                    .Join(people, user => user.PersonId, person => person.Id, (user, person) => person.FirstName + " " + person.LastName)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketingContactRow>> MarketingContactsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var consents = db.Set<Consent>();
        var users = db.Set<User>();
        return await (from person in db.Set<Person>().AsNoTracking()
                      join profile in db.Set<ClientProfile>() on person.Id equals profile.Id
                      where personIds.Contains(person.Id)
                      select new MarketingContactRow(
                          person.Id,
                          person.FirstName,
                          person.LastName,
                          person.Email,
                          users.Where(user => user.PersonId == person.Id).Select(user => user.LanguageCode).FirstOrDefault(),
                          consents.Where(consent => consent.PersonId == person.Id && consent.Purpose == ConsentPurpose.Marketing && consent.Channel == ConsentChannel.Email)
                              .OrderByDescending(consent => consent.RecordedAt).ThenByDescending(consent => consent.Id)
                              .Select(consent => consent.Granted).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public Task RemoveAssignmentsAsync(Guid tagId, CancellationToken cancellationToken) =>
        db.Set<PersonTag>().Where(assignment => assignment.TagId == tagId).ExecuteDeleteAsync(cancellationToken);

    public void Add(Tag tag) => db.Set<Tag>().Add(tag);

    public void Remove(Tag tag) => db.Set<Tag>().Remove(tag);

    public void Add(PersonTag assignment) => db.Set<PersonTag>().Add(assignment);

    public void Remove(PersonTag assignment) => db.Set<PersonTag>().Remove(assignment);

    public void Add(Consent consent) => db.Set<Consent>().Add(consent);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
#pragma warning restore CA1304, CA1311
