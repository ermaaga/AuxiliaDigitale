using Auxilia.Application.Cases;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Cases;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;

namespace Auxilia.Application.Tests.Cases;

public sealed class ServiceFolderTests : IAsyncDisposable
{
    private readonly InMemoryServiceCatalog data = new();
    private readonly ServiceFolderManager manager;
    private readonly ServiceFolderQueryService query;
    private readonly Guid service;

    public ServiceFolderTests()
    {
        manager = new ServiceFolderManager(ManagerHarness.Runner(), data);
        query = new ServiceFolderQueryService(data);
        var created = Service.Create(Guid.CreateVersion7(), new ServiceDetails("ISEE", null, 10m, 1, null, null, true)).Value;
        data.Services.Add(created);
        service = created.Id;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private async Task<Guid> AddAsync(string name, Guid? parent = null) =>
        (await manager.CreateAsync(service, new CreateServiceFolderRequest(name, parent), Ct)).Value;

    private async Task<string[]> PathsAsync() => (await query.ListAsync(service, Ct)).Value.Select(folder => $"{folder.Depth}:{folder.Path}").ToArray();

    [Fact]
    public async Task Tree_IsListedDepthFirst_WithPaths_NewFoldersLast()
    {
        var documents = await AddAsync(" Documenti ");
        await AddAsync("Ricevute");
        var identity = await AddAsync("Identità", documents);
        await AddAsync("Redditi", documents);
        await AddAsync("Carta", identity);

        (await PathsAsync()).ShouldBe(["1:Documenti", "2:Documenti / Identità", "3:Documenti / Identità / Carta", "2:Documenti / Redditi", "1:Ricevute"]);
    }

    [Fact]
    public async Task Names_AreUniqueAmongSiblings_AndChecked()
    {
        var documents = await AddAsync("Documenti");
        await AddAsync("Documenti", documents);

        (await manager.CreateAsync(service, new CreateServiceFolderRequest("DOCUMENTI", null), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderNameTaken);
        (await manager.CreateAsync(service, new CreateServiceFolderRequest("a/b", null), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderInvalid);
        (await manager.CreateAsync(service, new CreateServiceFolderRequest("x", Guid.CreateVersion7()), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["parentId"]);
        (await manager.CreateAsync(Guid.CreateVersion7(), new CreateServiceFolderRequest("x", null), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNotFound);

        var other = await AddAsync("Altro");
        (await manager.RenameAsync(service, other, "documenti", Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderNameTaken);
        (await manager.RenameAsync(service, other, " ", Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderInvalid);
        (await manager.RenameAsync(service, Guid.CreateVersion7(), "x", Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderNotFound);
        (await manager.RenameAsync(service, other, "Varie", Ct)).IsSuccess.ShouldBeTrue();
        (await PathsAsync()).ShouldContain("1:Varie");
    }

    [Fact]
    public async Task Depth_IsLimited()
    {
        Guid? parent = null;
        for (var level = 1; level <= ServiceFolder.MaxDepth; level++)
        {
            parent = await AddAsync("L" + level, parent);
        }

        (await manager.CreateAsync(service, new CreateServiceFolderRequest("Too deep", parent), Ct)).Error!.ValidationErrors["parentId"]
            .ShouldBe(["validation.serviceFolders.depth"]);
    }

    [Fact]
    public async Task Reorder_NeedsEverySiblingOnce()
    {
        var a = await AddAsync("A");
        var b = await AddAsync("B");
        var c = await AddAsync("C");

        (await manager.ReorderAsync(service, new ReorderServiceFoldersRequest(null, [c, a, b]), Ct)).IsSuccess.ShouldBeTrue();
        (await PathsAsync()).ShouldBe(["1:C", "1:A", "1:B"]);

        (await manager.ReorderAsync(service, new ReorderServiceFoldersRequest(null, [c, a]), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderInvalid);
        (await manager.ReorderAsync(service, new ReorderServiceFoldersRequest(null, [c, a, a]), Ct)).IsFailure.ShouldBeTrue();
        (await manager.ReorderAsync(service, new ReorderServiceFoldersRequest(Guid.CreateVersion7(), []), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderNotFound);
    }

    [Fact]
    public async Task Delete_RemovesTheSubtree_Only()
    {
        var documents = await AddAsync("Documenti");
        var identity = await AddAsync("Identità", documents);
        await AddAsync("Carta", identity);
        await AddAsync("Ricevute");

        (await manager.DeleteAsync(service, documents, Ct)).IsSuccess.ShouldBeTrue();

        (await PathsAsync()).ShouldBe(["1:Ricevute"]);
        (await manager.DeleteAsync(service, documents, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceFolderNotFound);
        (await query.ListAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNotFound);
    }
}
