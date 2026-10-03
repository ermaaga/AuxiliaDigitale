using Auxilia.Application.Abstractions.Documents;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Documents;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Documents;

internal sealed class DocumentDataFactory(ITenantDbContextFactory databases) : IDocumentDataFactory
{
    public async Task<IDocumentData> OpenAsync(CancellationToken cancellationToken) => new DocumentData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IDocumentData"/>
internal sealed class DocumentData(ITenantDbContext db) : IDocumentData
{
    public async Task<(IReadOnlyList<DocumentRow> Items, int Total)> PageAsync(DocumentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // People are joined (soft-delete filter): documents of deleted clients are not listed.
        var documents =
            from document in db.Set<Document>().AsNoTracking()
            join person in db.Set<Person>() on document.ClientId equals person.Id
            select new { document, person };

        var scope = filter.Scope;
        if (scope.EmployeeUserId is { } employee)
        {
            var cases = db.Set<Case>();
            var specializations = db.Set<Specialization>();
            var profiles = db.Set<ClientProfile>();
            var users = db.Set<User>();

            // F10: a case document as its case (D-04); without a case, client assigned or no private specialization outside the employee's.
            documents = documents.Where(row =>
                (row.document.CaseId != null && cases.Any(@case => @case.Id == row.document.CaseId
                    && (@case.SpecializationId == null
                        || specializations.Any(item => item.Id == @case.SpecializationId && (!item.IsPrivate || item.Members.Any(member => member.UserId == employee))))))
                || (row.document.CaseId == null
                    && (profiles.Any(profile => profile.Id == row.document.ClientId && profile.EmployeeUserId == employee)
                        || !specializations.Any(item => item.IsPrivate
                            && item.Members.Any(member => users.Any(user => user.Id == member.UserId && user.PersonId == row.document.ClientId))
                            && !item.Members.Any(member => member.UserId == employee)))));
        }
        else if (!scope.Everything)
        {
            documents = documents.Where(_ => false);
        }

        if (filter.ClientId is { } clientId)
        {
            documents = documents.Where(row => row.document.ClientId == clientId);
        }

        if (filter.CaseId is { } caseId)
        {
            documents = documents.Where(row => row.document.CaseId == caseId);
        }

        if (filter.FolderId is { } folderId)
        {
            documents = documents.Where(row => row.document.FolderId == folderId);
        }

        if (filter.ReferenceYear is { } year)
        {
            documents = documents.Where(row => row.document.ReferenceYear == year);
        }

        if (filter.AreaId is { } areaId)
        {
            documents = documents.Where(row => row.document.AreaId == areaId);
        }

        if (Pattern(filter.ClientName) is { } clientName)
        {
            documents = documents.Where(row => EF.Functions.ILike(row.person.FirstName + " " + row.person.LastName, clientName, "\\")
                || EF.Functions.ILike(row.person.LastName + " " + row.person.FirstName, clientName, "\\"));
        }

        if (Pattern(filter.FileName) is { } fileName)
        {
            documents = documents.Where(row => EF.Functions.ILike(row.document.FileName, fileName, "\\"));
        }

        if (Pattern(filter.Description) is { } description)
        {
            documents = documents.Where(row => row.document.Description != null && EF.Functions.ILike(row.document.Description, description, "\\"));
        }

        var names = UserNames();
        if (Pattern(filter.UploadedBy) is { } uploadedBy)
        {
            documents = documents.Where(row => names.Any(name => name.UserId == row.document.UploadedByUserId && EF.Functions.ILike(name.FullName, uploadedBy, "\\")));
        }

        var total = await documents.CountAsync(cancellationToken);
        var areas = db.Set<DocumentArea>();
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (DocumentSort.Client, false) => documents.OrderBy(row => row.person.LastName).ThenBy(row => row.person.FirstName),
            (DocumentSort.Client, true) => documents.OrderByDescending(row => row.person.LastName).ThenByDescending(row => row.person.FirstName),
            (DocumentSort.FileName, false) => documents.OrderBy(row => row.document.FileName),
            (DocumentSort.FileName, true) => documents.OrderByDescending(row => row.document.FileName),
            (DocumentSort.ReferenceYear, false) => documents.OrderBy(row => row.document.ReferenceYear),
            (DocumentSort.ReferenceYear, true) => documents.OrderByDescending(row => row.document.ReferenceYear),
            (DocumentSort.Area, false) => documents.OrderBy(row => areas.Where(area => area.Id == row.document.AreaId).Select(area => area.Name).FirstOrDefault()),
            (DocumentSort.Area, true) => documents.OrderByDescending(row => areas.Where(area => area.Id == row.document.AreaId).Select(area => area.Name).FirstOrDefault()),
            (DocumentSort.UploadedBy, false) => documents.OrderBy(row => names.Where(name => name.UserId == row.document.UploadedByUserId).Select(name => name.FullName).FirstOrDefault()),
            (DocumentSort.UploadedBy, true) => documents.OrderByDescending(row => names.Where(name => name.UserId == row.document.UploadedByUserId).Select(name => name.FullName).FirstOrDefault()),
            (_, false) => documents.OrderBy(row => row.document.UploadedAt),
            (_, true) => documents.OrderByDescending(row => row.document.UploadedAt),
        };

        var page = await sorted
            .ThenBy(row => row.document.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(row => row.document)
            .ToListAsync(cancellationToken);
        return (await WithNamesAsync(page, cancellationToken), total);
    }

    public async Task<DocumentRow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await (
                from item in db.Set<Document>().AsNoTracking()
                join person in db.Set<Person>() on item.ClientId equals person.Id
                where item.Id == id
                select item)
            .SingleOrDefaultAsync(cancellationToken);
        return document is null ? null : (await WithNamesAsync([document], cancellationToken))[0];
    }

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Document>().SingleOrDefaultAsync(document => document.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Document>> OfCaseAsync(Guid caseId, CancellationToken cancellationToken) =>
        await db.Set<Document>().AsNoTracking().Where(document => document.CaseId == caseId).ToListAsync(cancellationToken);

    public async Task<DocumentClientAccess> ClientAccessAsync(Guid clientId, Guid employeeUserId, CancellationToken cancellationToken)
    {
        var assigned = await db.Set<ClientProfile>().AnyAsync(profile => profile.Id == clientId && profile.EmployeeUserId == employeeUserId, cancellationToken);
        var clientUsers = db.Set<User>().Where(user => user.PersonId == clientId).Select(user => user.Id);
        var outside = await db.Set<Specialization>().AnyAsync(
            specialization => specialization.IsPrivate
                && specialization.Members.Any(member => clientUsers.Contains(member.UserId))
                && !specialization.Members.Any(member => member.UserId == employeeUserId),
            cancellationToken);
        return new DocumentClientAccess(assigned, outside);
    }

    public async Task<IReadOnlySet<string>> NamesTakenAsync(
        Guid clientId, Guid? caseId, Guid? folderId, IReadOnlyCollection<string> fileNames, Guid? exceptId, CancellationToken cancellationToken)
    {
        var wanted = fileNames.Select(name => name.ToLowerInvariant()).ToArray();
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(): no culture involved.
        var taken = await db.Set<Document>()
            .Where(document => document.ClientId == clientId && document.CaseId == caseId && document.FolderId == folderId && document.Id != exceptId
                && wanted.Contains(document.FileName.ToLower()))
#pragma warning restore CA1304, CA1311
            .Select(document => document.FileName)
            .ToListAsync(cancellationToken);
        return taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<DocumentAreaRow>> AreasAsync(CancellationToken cancellationToken)
    {
        var documents = db.Set<Document>();
        return await db.Set<DocumentArea>().AsNoTracking()
            .OrderBy(area => area.Name)
            .ThenBy(area => area.Id)
            .Select(area => new DocumentAreaRow(area, documents.Count(document => document.AreaId == area.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<DocumentArea?> FindAreaAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<DocumentArea>().SingleOrDefaultAsync(area => area.Id == id, cancellationToken);

    // name is citext: equality is case-insensitive in the database.
    public Task<bool> AreaNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<DocumentArea>().AnyAsync(area => area.IsActive && area.Name == name && area.Id != exceptId, cancellationToken);

    public void Add(Document document) => db.Set<Document>().Add(document);

    public void Remove(Document document) => db.Set<Document>().Remove(document);

    public void Add(DocumentArea area) => db.Set<DocumentArea>().Add(area);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>A "contains" ILIKE pattern with the wildcards of the text escaped; <c>null</c> for no filter.</summary>
    private static string? Pattern(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    /// <summary>User id → full name (people not deleted; the names of the page are read apart, deleted people included).</summary>
    private IQueryable<UserName> UserNames() =>
        from user in db.Set<User>()
        join person in db.Set<Person>() on user.PersonId equals person.Id
        select new UserName(user.Id, person.FirstName + " " + person.LastName);

    /// <summary>
    /// The names of a page in separate queries: <c>IgnoreQueryFilters</c> (deleted clients, cases and uploaders keep their
    /// names) applies to a whole query, never to the list query.
    /// </summary>
    private async Task<IReadOnlyList<DocumentRow>> WithNamesAsync(List<Document> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var clientIds = documents.Select(document => document.ClientId).Distinct().ToArray();
        var caseIds = documents.Select(document => document.CaseId).OfType<Guid>().Distinct().ToArray();
        var areaIds = documents.Select(document => document.AreaId).OfType<Guid>().Distinct().ToArray();
        var userIds = documents.Select(document => document.UploadedByUserId).OfType<Guid>().Distinct().ToArray();

        var clients = await db.Set<Person>().IgnoreQueryFilters().AsNoTracking()
            .Where(person => clientIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, person => person.FirstName + " " + person.LastName, cancellationToken);
        var cases = await (
                from @case in db.Set<Case>().IgnoreQueryFilters().AsNoTracking()
                where caseIds.Contains(@case.Id)
                join service in db.Set<Service>().IgnoreQueryFilters() on @case.ServiceId equals service.Id
                select new { @case.Id, @case.Number, service.Name })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var areas = await db.Set<DocumentArea>().AsNoTracking()
            .Where(area => areaIds.Contains(area.Id))
            .ToDictionaryAsync(area => area.Id, area => area.Name, cancellationToken);
        var uploaders = await (
                from user in db.Set<User>()
                where userIds.Contains(user.Id)
                join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
                select new { user.Id, Name = person.FirstName + " " + person.LastName })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

        return documents.Select(document => new DocumentRow(
                document,
                clients.GetValueOrDefault(document.ClientId, string.Empty),
                document.CaseId is { } caseId && cases.TryGetValue(caseId, out var found) ? found.Number : null,
                document.CaseId is { } serviceCase && cases.TryGetValue(serviceCase, out var withService) ? withService.Name : null,
                document.AreaId is { } areaId ? areas.GetValueOrDefault(areaId) : null,
                document.UploadedByUserId is { } uploader ? uploaders.GetValueOrDefault(uploader) : null))
            .ToArray();
    }

    private sealed record UserName(Guid UserId, string FullName);
}
