using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Cases;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Cases;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;

namespace Auxilia.Application.Tests.Cases;

public sealed class ServiceCatalogTests : IAsyncDisposable
{
    private readonly InMemoryServiceCatalog data = new();
    private readonly ServiceCatalogManager manager;
    private readonly ServiceCatalogQueryService query;

    public ServiceCatalogTests()
    {
        manager = new ServiceCatalogManager(ManagerHarness.Runner(), data);
        query = new ServiceCatalogQueryService(data);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private static CreateServiceRequest Request(string name = "ISEE", Guid? category = null, Guid? specialization = null) =>
        new(name, "Indicatore", 10m, 1, category, specialization);

    private async Task<Guid> CategoryAsync(string name = "Fiscale") =>
        (await manager.CreateCategoryAsync(new CreateServiceCategoryRequest(name, null), Ct)).Value;

    [Fact]
    public async Task CreateService_WithCategoryAndSpecialization_KeepsBoth()
    {
        var category = await CategoryAsync();
        var specialization = data.AddSpecialization();

        var id = (await manager.CreateServiceAsync(Request(category: category, specialization: specialization), Ct)).Value;

        var service = (await query.GetAsync(id, Ct)).Value;
        (service.Name, service.Price, service.Currency, service.IsActive).ShouldBe(("ISEE", 10m, "EUR", true));
        (service.Category!.Id, service.Category.Name, service.Specialization!.Id).ShouldBe((category, "Fiscale", specialization));
    }

    [Fact]
    public async Task CreateService_UnknownOrInactiveReferences_AreFieldErrors()
    {
        var inactive = await CategoryAsync();
        (await manager.UpdateCategoryAsync(inactive, new UpdateServiceCategoryRequest("Fiscale", null, false), Ct)).IsSuccess.ShouldBeTrue();

        var result = await manager.CreateServiceAsync(Request(category: inactive, specialization: Guid.CreateVersion7()), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Cases.ServiceInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["categoryId", "specializationId"], ignoreOrder: true);
        data.Services.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateService_KeepsAReferenceThatBecameInactive_AndDeactivates()
    {
        var category = await CategoryAsync();
        var specialization = data.AddSpecialization();
        var id = (await manager.CreateServiceAsync(Request(category: category, specialization: specialization), Ct)).Value;
        (await manager.UpdateCategoryAsync(category, new UpdateServiceCategoryRequest("Fiscale", null, false), Ct)).IsSuccess.ShouldBeTrue();
        data.ActiveSpecializations.Clear();

        var updated = await manager.UpdateServiceAsync(id, new UpdateServiceRequest("ISEE", null, 12.5m, 30, category, specialization, false), Ct);

        updated.IsSuccess.ShouldBeTrue();
        var service = data.Services.Single();
        (service.Price, service.DurationDays, service.IsActive, service.CategoryId, service.SpecializationId).ShouldBe((12.5m, 30, false, category, specialization));
    }

    [Fact]
    public async Task ServiceNames_AreUnique_AndUnknownServicesAreNotFound()
    {
        var first = (await manager.CreateServiceAsync(Request(), Ct)).Value;
        var second = (await manager.CreateServiceAsync(Request("730"), Ct)).Value;

        (await manager.CreateServiceAsync(Request("isee"), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNameTaken);
        (await manager.UpdateServiceAsync(second, new UpdateServiceRequest("ISEE", null, 1m, 1, null, null, true), Ct)).Error!.Code
            .ShouldBe(EventCodes.Cases.ServiceNameTaken);
        (await manager.UpdateServiceAsync(Guid.CreateVersion7(), new UpdateServiceRequest("X", null, 1m, 1, null, null, true), Ct)).Error!.Code
            .ShouldBe(EventCodes.Cases.ServiceNotFound);
        (await manager.DeleteServiceAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNotFound);
        (await query.GetAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceNotFound);

        data.WithCases.Add(first);
        (await manager.DeleteServiceAsync(first, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceInUse);
        data.WithCases.Clear();
        (await manager.DeleteServiceAsync(first, Ct)).IsSuccess.ShouldBeTrue();
        (await query.GetAsync(first, Ct)).IsFailure.ShouldBeTrue();
        (await manager.CreateServiceAsync(Request(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Categories_NamesUniqueAmongActive_DeleteOnlyWhenUnused()
    {
        var used = await CategoryAsync();
        var unused = await CategoryAsync("Lavoro");
        (await manager.CreateServiceAsync(Request(category: used), Ct)).IsSuccess.ShouldBeTrue();

        (await manager.CreateCategoryAsync(new CreateServiceCategoryRequest("fiscale", null), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceCategoryNameTaken);
        (await manager.CreateCategoryAsync(new CreateServiceCategoryRequest("", null), Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceCategoryInvalid);
        (await manager.UpdateCategoryAsync(unused, new UpdateServiceCategoryRequest("Fiscale", null, true), Ct)).Error!.Code
            .ShouldBe(EventCodes.Cases.ServiceCategoryNameTaken);
        (await manager.UpdateCategoryAsync(Guid.CreateVersion7(), new UpdateServiceCategoryRequest("X", null, true), Ct)).Error!.Code
            .ShouldBe(EventCodes.Cases.ServiceCategoryNotFound);

        (await manager.DeleteCategoryAsync(used, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceCategoryInUse);
        (await manager.DeleteCategoryAsync(unused, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.DeleteCategoryAsync(unused, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.ServiceCategoryNotFound);

        var categories = await query.CategoriesAsync(Ct);
        categories.ShouldHaveSingleItem().ServiceCount.ShouldBe(1);
    }

    [Fact]
    public async Task List_ValidatesTheQuery_AndPassesTheFilter()
    {
        await manager.CreateServiceAsync(Request(), Ct);
        var category = Guid.CreateVersion7();

        var page = (await query.ListAsync(new ServiceListQuery(" ise ", category, null, true, "-price", 2, 10), Ct)).Value;

        page.TotalCount.ShouldBe(1);
        var filter = data.LastFilter!;
        (filter.Name, filter.CategoryId, filter.Active, filter.Sort, filter.Descending, filter.Skip, filter.Take)
            .ShouldBe(("ise", (Guid?)category, (bool?)true, ServiceSort.Price, true, 10, 10));

        var invalid = await query.ListAsync(new ServiceListQuery(new string('x', 201), null, null, null, "age", 0, 500), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "search"], ignoreOrder: true);
    }
}

/// <summary>The catalog in memory: the page applies no filter (the SQL filters are tested on PostgreSQL).</summary>
internal sealed class InMemoryServiceCatalog : IServiceCatalogDataFactory, IServiceCatalogData
{
    public List<ServiceCategory> Categories { get; } = [];

    public List<Service> Services { get; } = [];

    public List<ServiceFolder> Folders { get; } = [];

    public HashSet<Guid> Deleted { get; } = [];

    public HashSet<Guid> ActiveSpecializations { get; } = [];

    /// <summary>Services with cases not deleted.</summary>
    public HashSet<Guid> WithCases { get; } = [];

    public ServiceFilter? LastFilter { get; private set; }

    public Guid AddSpecialization()
    {
        var id = Guid.CreateVersion7();
        ActiveSpecializations.Add(id);
        return id;
    }

    public Task<IServiceCatalogData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IServiceCatalogData>(this);

    public Task<IReadOnlyList<ServiceCategoryRow>> CategoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceCategoryRow>>(Categories
            .Select(category => new ServiceCategoryRow(category, Live.Count(service => service.CategoryId == category.Id)))
            .ToArray());

    public Task<ServiceCategory?> FindCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Categories.SingleOrDefault(category => category.Id == id));

    public Task<bool> CategoryNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Categories.Any(category => category.IsActive && string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase) && category.Id != exceptId));

    public Task<bool> CategoryInUseAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Services.Any(service => service.CategoryId == id));

    public Task<(IReadOnlyList<ServiceRow> Items, int Total)> PageAsync(ServiceFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        var rows = Live.Select(Row).ToArray();
        return Task.FromResult<(IReadOnlyList<ServiceRow>, int)>((rows, rows.Length));
    }

    public Task<ServiceRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Live.SingleOrDefault(service => service.Id == id) is { } service ? Row(service) : null);

    public Task<Service?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Live.SingleOrDefault(service => service.Id == id));

    public Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Live.Any(service => string.Equals(service.Name, name, StringComparison.OrdinalIgnoreCase) && service.Id != exceptId));

    public Task<bool> ServiceHasCasesAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(WithCases.Contains(id));

    public Task<bool> IsActiveCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Categories.Any(category => category.Id == id && category.IsActive));

    public Task<bool> IsActiveEmployeeSpecializationAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(ActiveSpecializations.Contains(id));

    public Task<IReadOnlyList<ServiceFolder>> FoldersAsync(Guid serviceId, bool readOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceFolder>>(Folders.Where(folder => folder.ServiceId == serviceId).ToArray());

    public void Add(ServiceFolder folder) => Folders.Add(folder);

    public void Remove(ServiceFolder folder) => Folders.Remove(folder);

    public void Add(ServiceCategory category) => Categories.Add(category);

    public void Add(Service service) => Services.Add(service);

    public void Remove(ServiceCategory category) => Categories.Remove(category);

    public void Remove(Service service) => Deleted.Add(service.Id);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IEnumerable<Service> Live => Services.Where(service => !Deleted.Contains(service.Id));

    private ServiceRow Row(Service service)
    {
        var category = Categories.SingleOrDefault(item => item.Id == service.CategoryId);
        return new ServiceRow(service, category?.Name, category?.IsActive ?? false, service.SpecializationId is null ? null : "Fisco", false);
    }
}
