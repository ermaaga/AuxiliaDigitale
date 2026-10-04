using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Directory;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;

using NSubstitute;

namespace Auxilia.Application.Tests.Directory;

public sealed class ConsentTagTests : IAsyncDisposable
{
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid OtherClient = Guid.CreateVersion7();
    private static readonly Guid Staff = Guid.CreateVersion7();

    private readonly InMemoryConsentTags data = new();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly TagManager tags;
    private readonly TagQueryService tagQuery;
    private readonly ConsentManager consents;
    private readonly ConsentQueryService consentQuery;

    public ConsentTagTests()
    {
        caller.UserId.Returns(Staff);
        data.Clients.AddRange([Client, OtherClient]);
        tags = new TagManager(ManagerHarness.Runner(), data, caller, clock);
        tagQuery = new TagQueryService(data);
        consents = new ConsentManager(ManagerHarness.Runner(), data, caller, clock);
        consentQuery = new ConsentQueryService(data);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    [Fact]
    public async Task Tags_HaveUniqueNamesAndColours()
    {
        var caf = (await tags.CreateAsync(new SaveTagRequest(" CAF ", "#72fa29"), Ct)).Value;
        data.Tags.Single().ShouldSatisfyAllConditions(tag => tag.Name.ShouldBe("CAF"), tag => tag.Color.ShouldBe("#72FA29"));

        (await tags.CreateAsync(new SaveTagRequest("caf", null), Ct)).Error!.ValidationErrors["name"].ShouldBe(["validation.tags.nameTaken"]);
        (await tags.CreateAsync(new SaveTagRequest(" ", "red"), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["name", "color"], ignoreOrder: true);
        var vip = (await tags.CreateAsync(new SaveTagRequest("VIP", null), Ct)).Value;
        (await tags.UpdateAsync(vip, new SaveTagRequest("CAF", null), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.TagInvalid);
        (await tags.UpdateAsync(vip, new SaveTagRequest("Gold", "#000000"), Ct)).IsSuccess.ShouldBeTrue();
        (await tags.UpdateAsync(Guid.CreateVersion7(), new SaveTagRequest("X", null), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.TagNotFound);

        await tags.SetClientTagsAsync(Client, [caf, vip], Ct);
        (await tagQuery.ListAsync(Ct)).Select(tag => (tag.Name, tag.ClientCount)).ShouldBe([("CAF", 1), ("Gold", 1)]);
        (await tags.DeleteAsync(caf, Ct)).IsSuccess.ShouldBeTrue();
        (await tagQuery.ClientTagsAsync(Client, Ct)).Value.Select(tag => tag.Name).ShouldBe(["Gold"]);
    }

    [Fact]
    public async Task ClientTags_AreSetOrChangedInBulk()
    {
        var caf = (await tags.CreateAsync(new SaveTagRequest("CAF", null), Ct)).Value;
        var vip = (await tags.CreateAsync(new SaveTagRequest("VIP", null), Ct)).Value;

        (await tags.SetClientTagsAsync(Client, [caf, caf], Ct)).IsSuccess.ShouldBeTrue();
        data.Assignments.ShouldHaveSingleItem().AssignedByUserId.ShouldBe(Staff);
        (await tags.SetClientTagsAsync(Client, [Guid.CreateVersion7()], Ct)).Error!.ValidationErrors["tagIds"].ShouldBe(["validation.tags.unknown"]);
        (await tags.SetClientTagsAsync(Guid.CreateVersion7(), [caf], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);

        var changed = await tags.BulkAsync(new BulkClientTagsRequest([Client, OtherClient, Guid.CreateVersion7()], [vip], [caf]), Ct);
        changed.Value.ShouldBe(3);
        data.Assignments.Select(item => (item.PersonId, item.TagId)).ShouldBe([(Client, vip), (OtherClient, vip)], ignoreOrder: true);
        (await tags.BulkAsync(new BulkClientTagsRequest([], [vip], null), Ct)).Error!.ValidationErrors.ShouldContainKey("clientIds");
        (await tags.BulkAsync(new BulkClientTagsRequest([Client], [Guid.CreateVersion7()], null), Ct)).Error!.ValidationErrors.ShouldContainKey("tags");
    }

    [Fact]
    public async Task Consents_KeepTheHistory_AndTheLatestChangeIsCurrent()
    {
        (await consents.RecordAsync(Client, new RecordConsentRequest("marketing", "email", true, "v1", "on the phone"), Ct)).IsSuccess.ShouldBeTrue();
        clock.Advance(TimeSpan.FromDays(1));
        await consents.RecordAsync(Client, ConsentPurpose.Marketing, ConsentChannel.Email, false, ConsentSource.Import, null, null, Ct);

        var result = (await consentQuery.GetAsync(Client, Ct)).Value;
        result.Current.Select(state => (state.Purpose, state.Channel, state.Granted, state.Source)).ShouldBe(
        [
            ("Marketing", "Email", false, "Import"), ("Marketing", "WhatsApp", false, null), ("Privacy", "Email", false, null), ("Privacy", "WhatsApp", false, null),
        ]);
        result.History.Select(change => (change.Granted, change.Source, change.Version)).ShouldBe([(false, "Import", null), (true, "Staff", "v1")]);

        (await consents.RecordAsync(Client, new RecordConsentRequest("newsletter", "fax", true, new string('v', 51), null), Ct))
            .Error!.ValidationErrors.Keys.ShouldBe(["purpose", "channel", "version"], ignoreOrder: true);
        (await consents.RecordAsync(Guid.CreateVersion7(), new RecordConsentRequest("Marketing", "Email", true, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Directory.ClientNotFound);
        (await consentQuery.GetAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.ClientNotFound);
    }

    [Fact]
    public async Task ClientImport_SetsTagsAndTheMarketingConsent()
    {
        var caf = (await tags.CreateAsync(new SaveTagRequest("CAF", null), Ct)).Value;
        var clients = Substitute.For<IClientManager>();
        clients.CreateAsync(Arg.Any<CreateClientRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new CreateClientResponse(Client, true, null)));
        var lookups = Substitute.For<IImportLookups>();
        lookups.TakenFiscalCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        lookups.TakenUserNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        lookups.EmployeesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, Guid>());
        var target = new ClientImportTarget(clients, lookups, data, tags, consents, clock);
        var row = new ImportRow(2, new Dictionary<string, string>
        {
            ["firstName"] = "Mario", ["lastName"] = "Rossi", ["email"] = "m@example.test", ["fiscalCode"] = "RSSMRA80A01H501U", ["birthDate"] = "1980-01-01",
            ["tags"] = "caf; Nessuno", ["marketingEmailConsent"] = "sì",
        });

        var errors = await target.ValidateAsync([row], Ct);
        errors[0].Errors["tags"].ShouldBe([ImportValues.NotFound]);

        var valid = row with { Values = new Dictionary<string, string>(row.Values) { ["tags"] = "caf" } };
        (await target.ImportAsync(valid, Ct)).Value.ShouldBe(Client);
        data.Assignments.ShouldHaveSingleItem().TagId.ShouldBe(caf);
        data.Consents.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            consent => consent.Granted.ShouldBeTrue(), consent => consent.Source.ShouldBe(ConsentSource.Import));
    }
}

/// <summary>Tags and consents in memory.</summary>
internal sealed class InMemoryConsentTags : IConsentTagDataFactory, IConsentTagData
{
    public List<Guid> Clients { get; } = [];

    public List<Tag> Tags { get; } = [];

    public List<PersonTag> Assignments { get; } = [];

    public List<Consent> Consents { get; } = [];

    public Task<IConsentTagData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IConsentTagData>(this);

    public Task<IReadOnlyList<TagRow>> TagsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TagRow>>([.. Tags.OrderBy(tag => tag.Name, StringComparer.Ordinal)
            .Select(tag => new TagRow(tag.Id, tag.Name, tag.Color, Assignments.Count(item => item.TagId == tag.Id)))]);

    public Task<Tag?> FindTagAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Tags.SingleOrDefault(tag => tag.Id == id));

    public Task<bool> TagNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Tags.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase) && tag.Id != exceptId));

    public Task<IReadOnlyList<Tag>> FindTagsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Tag>>([.. Tags.Where(tag => ids.Contains(tag.Id))]);

    public Task<IReadOnlyDictionary<string, Guid>> TagIdsByNameAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Guid>>(Tags.Where(tag => names.Contains(tag.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(tag => tag.Name, tag => tag.Id, StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<Tag>> TagsOfAsync(Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Tag>>([.. Tags.Where(tag => Assignments.Any(item => item.PersonId == personId && item.TagId == tag.Id))]);

    public Task<IReadOnlyList<PersonTag>> AssignmentsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersonTag>>([.. Assignments.Where(item => personIds.Contains(item.PersonId))]);

    public Task<IReadOnlyList<Guid>> ExistingClientsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Clients.Where(ids.Contains)]);

    public Task<IReadOnlyList<ConsentRow>> ConsentsAsync(Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConsentRow>>([.. Consents.Where(item => item.PersonId == personId).OrderByDescending(item => item.RecordedAt)
            .Select(item => new ConsentRow(item.Id, item.Purpose, item.Channel, item.Granted, item.Source, item.Version, item.Note, item.RecordedAt, item.RecordedByUserId, "Staff"))]);

    public Task<IReadOnlyList<MarketingContactRow>> MarketingContactsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MarketingContactRow>>([.. Clients.Where(personIds.Contains).Select(id => new MarketingContactRow(
            id, "Mario", "Rossi", "mario@example.test", "it",
            Consents.Where(consent => consent.PersonId == id && consent.Purpose == ConsentPurpose.Marketing && consent.Channel == ConsentChannel.Email)
                .OrderByDescending(consent => consent.RecordedAt).Select(consent => consent.Granted).FirstOrDefault()))]);

    public Task RemoveAssignmentsAsync(Guid tagId, CancellationToken cancellationToken)
    {
        Assignments.RemoveAll(item => item.TagId == tagId);
        return Task.CompletedTask;
    }

    public void Add(Tag tag) => Tags.Add(tag);

    public void Remove(Tag tag) => Tags.Remove(tag);

    public void Add(PersonTag assignment) => Assignments.Add(assignment);

    public void Remove(PersonTag assignment) => Assignments.Remove(assignment);

    public void Add(Consent consent) => Consents.Add(consent);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
