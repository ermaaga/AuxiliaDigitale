using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Cases;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Cases;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;

using NSubstitute;

namespace Auxilia.Application.Tests.Cases;

public sealed class ChecklistTests : IAsyncDisposable
{
    private readonly InMemoryChecklists checklists = new();
    private readonly IServiceCatalogDataFactory catalogFactory = Substitute.For<IServiceCatalogDataFactory>();
    private readonly IServiceCatalogData catalog = Substitute.For<IServiceCatalogData>();
    private readonly Guid service = Guid.CreateVersion7();
    private readonly Guid folder = Guid.CreateVersion7();
    private readonly ServiceChecklistManager manager;
    private readonly ServiceChecklistQueryService query;

    public ChecklistTests()
    {
        catalogFactory.OpenAsync(Arg.Any<CancellationToken>()).Returns(catalog);
        var found = Service.Create(service, new ServiceDetails("730", null, 50m, 30, null, null, true)).Value;
        catalog.FindAsync(service, Arg.Any<CancellationToken>()).Returns(found);
        catalog.GetAsync(service, Arg.Any<CancellationToken>()).Returns(new ServiceRow(found, null, false, null, false));
        catalog.FoldersAsync(service, true, Arg.Any<CancellationToken>()).Returns([ServiceFolder.Create(folder, service, null, "Documenti", 1).Value]);
        manager = new ServiceChecklistManager(ManagerHarness.Runner(), catalogFactory, checklists);
        query = new ServiceChecklistQueryService(catalogFactory, checklists);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await checklists.DisposeAsync();
        await catalog.DisposeAsync();
    }

    [Fact]
    public async Task Save_ReplacesTheChecklist_KeepingTheItemsGivenById()
    {
        (await manager.SaveAsync(service, new SaveServiceChecklistRequest([new(null, "Documento d'identità", folder, true), new(null, "CU", null, false)]), Ct))
            .IsSuccess.ShouldBeTrue();
        var identity = checklists.Items.Single(item => item.Name == "Documento d'identità");
        checklists.Marks.Add(new CaseChecklistMark(Guid.CreateVersion7(), identity.Id, null, DateTimeOffset.UtcNow));

        (await manager.SaveAsync(service, new SaveServiceChecklistRequest([new(null, "Modello 730 precedente", null, true), new(identity.Id, "Carta d'identità", folder, true)]), Ct))
            .IsSuccess.ShouldBeTrue();

        (await query.ListAsync(service, Ct)).Value.Select(item => (item.Name, item.Required)).ShouldBe([("Modello 730 precedente", true), ("Carta d'identità", true)]);
        checklists.Items.ShouldContain(item => item.Id == identity.Id);
        checklists.Marks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Save_ChecksNamesAndFolders()
    {
        var result = await manager.SaveAsync(
            service, new SaveServiceChecklistRequest([new(null, "CU", null, true), new(null, "cu", null, false), new(null, " ", Guid.CreateVersion7(), false)]), Ct);

        result.Error!.ValidationErrors.Select(pair => (pair.Key, pair.Value.Single())).ShouldBe(
            [("items[1].name", "validation.checklist.duplicate"), ("items[2].name", "validation.checklist.name"), ("items[2].folderId", "validation.checklist.folder")],
            ignoreOrder: true);
        (await manager.SaveAsync(Guid.CreateVersion7(), new SaveServiceChecklistRequest([]), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNotFound);
        (await query.ListAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNotFound);
    }
}
