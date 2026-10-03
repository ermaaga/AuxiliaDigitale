using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Services;

/// <summary>
/// B-07 over HTTP (F08, Q26–Q28): categories and services managed by an Administrator, read by an Employee, refused
/// to a Client; filters, sorting, active references, unique names, soft delete.
/// </summary>
public sealed class ServiceEndpointsTests(ServiceEndpointsTests.Factory factory) : IClassFixture<ServiceEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_ManagesTheCatalog_EmployeeReadsIt_ClientCannot()
    {
        var (admin, employee, client) = await UsersAsync();
        var specialization = await Factory.AddSpecializationAsync(TenantRole.Employee);
        var categoryName = "Fiscale " + Guid.NewGuid().ToString("N")[..6];

        using var createdCategory = await SendAsync(HttpMethod.Post, "/api/v1/service-categories", admin, new CreateServiceCategoryRequest(categoryName, "Pratiche fiscali"));
        createdCategory.StatusCode.ShouldBe(HttpStatusCode.Created, await createdCategory.Content.ReadAsStringAsync(Ct));
        var category = (await createdCategory.Content.ReadFromJsonAsync<ServiceCategoryResponse>(Ct))!;
        (category.Name, category.IsActive, category.ServiceCount).ShouldBe((categoryName, true, 0));

        var name = "ISEE " + Guid.NewGuid().ToString("N")[..6];
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/services", admin,
            new CreateServiceRequest(name, "Indicatore", 10.5m, 1, category.Id, specialization));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var service = (await created.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!;
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/services/{service.Id}");
        (service.Price, service.Currency, service.Category!.Name, service.Specialization!.Id).ShouldBe((10.5m, "EUR", categoryName, specialization));

        // Employees read the catalog (they open cases), but do not change it.
        var page = await ListAsync(employee, $"filter[name]={Uri.EscapeDataString(name)}&filter[categoryId]={category.Id}&filter[active]=true");
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(service.Id);
        (await SendAsync(HttpMethod.Post, "/api/v1/services", employee, new CreateServiceRequest("X", null, 1m, 1, null, null))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/services", client)).StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // The category in use cannot be deleted: deactivated, it stays on the service.
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/service-categories/{category.Id}", admin),
            HttpStatusCode.Conflict, EventCodes.Cases.ServiceCategoryInUse);
        using (var deactivated = await SendAsync(HttpMethod.Put, $"/api/v1/service-categories/{category.Id}", admin,
            new UpdateServiceCategoryRequest(categoryName, null, false)))
        {
            (await deactivated.Content.ReadFromJsonAsync<ServiceCategoryResponse>(Ct))!.ShouldBe(category with { Description = null, IsActive = false, ServiceCount = 1 });
        }

        using (var updated = await SendAsync(HttpMethod.Put, $"/api/v1/services/{service.Id}", admin,
            new UpdateServiceRequest(name, null, 0m, 365, category.Id, specialization, false)))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
            var body = (await updated.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!;
            (body.IsActive, body.DurationDays, body.Category!.IsActive).ShouldBe((false, 365, false));
        }

        // An inactive category cannot be given to another service.
        using var refused = await SendAsync(HttpMethod.Post, "/api/v1/services", admin,
            new CreateServiceRequest("Other " + Guid.NewGuid().ToString("N")[..6], null, 1m, 1, category.Id, Guid.NewGuid()));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").EnumerateObject().Select(field => field.Name)
            .ShouldBe(["categoryId", "specializationId"], ignoreOrder: true);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/services/{service.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/services/{service.Id}", employee), HttpStatusCode.NotFound, EventCodes.Cases.ServiceNotFound);

        // The deleted service still holds the category (foreign key): it stays in use.
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Delete, $"/api/v1/service-categories/{category.Id}", admin),
            HttpStatusCode.Conflict, EventCodes.Cases.ServiceCategoryInUse);
    }

    [Fact]
    public async Task Validation_UniqueNames_AndSorting()
    {
        var (admin, _, _) = await UsersAsync();
        var prefix = "Sort-" + Guid.NewGuid().ToString("N")[..6];
        foreach (var (suffix, price) in new[] { ("A", 30m), ("B", 10m), ("C", 20m) })
        {
            (await SendAsync(HttpMethod.Post, "/api/v1/services", admin, new CreateServiceRequest($"{prefix} {suffix}", null, price, 7, null, null))).StatusCode
                .ShouldBe(HttpStatusCode.Created);
        }

        var byPrice = await ListAsync(admin, $"filter[name]={prefix}&sort=-price");
        byPrice.Items.Select(item => item.Price).ShouldBe([30m, 20m, 10m]);
        byPrice.TotalCount.ShouldBe(3);

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/services", admin, new CreateServiceRequest($"{prefix} a".ToUpperInvariant(), null, 1m, 1, null, null)),
            HttpStatusCode.Conflict, EventCodes.Cases.ServiceNameTaken);

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/services", admin, new CreateServiceRequest("", null, -1m, 0, null, null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe($"AUX-{EventCodes.Cases.ServiceInvalid}");
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["name", "price", "durationDays"], ignoreOrder: true);

        (await SendAsync(HttpMethod.Get, "/api/v1/services?sort=age&pageSize=500", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FolderTemplate_AdministratorEditsIt_EmployeeReadsIt()
    {
        var (admin, employee, _) = await UsersAsync();
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/services", admin,
            new CreateServiceRequest("Folders " + Guid.NewGuid().ToString("N")[..6], null, 1m, 1, null, null));
        var service = (await created.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!.Id;
        var path = $"/api/v1/services/{service}/folders";

        static async Task<ServiceFolderResponse[]> TreeAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
        {
            using (response)
            {
                response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
                return (await response.Content.ReadFromJsonAsync<ServiceFolderResponse[]>(Ct))!;
            }
        }

        var documents = (await TreeAsync(await SendAsync(HttpMethod.Post, path, admin, new CreateServiceFolderRequest("Documenti", null)), HttpStatusCode.Created)).Single().Id;
        var receipts = (await TreeAsync(await SendAsync(HttpMethod.Post, path, admin, new CreateServiceFolderRequest("Ricevute", null)), HttpStatusCode.Created))
            .Single(folder => folder.Name == "Ricevute").Id;
        var identity = (await TreeAsync(await SendAsync(HttpMethod.Post, path, admin, new CreateServiceFolderRequest("Identità", documents)), HttpStatusCode.Created))
            .Single(folder => folder.Name == "Identità").Id;
        await TreeAsync(await SendAsync(HttpMethod.Post, path, admin, new CreateServiceFolderRequest("Carta", identity)), HttpStatusCode.Created);

        var tree = await TreeAsync(await SendAsync(HttpMethod.Get, path, employee));
        tree.Select(folder => folder.Path).ShouldBe(["Documenti", "Documenti / Identità", "Documenti / Identità / Carta", "Ricevute"]);
        (await SendAsync(HttpMethod.Post, path, employee, new CreateServiceFolderRequest("X", null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, path, admin, new CreateServiceFolderRequest("documenti", null)),
            HttpStatusCode.Conflict, EventCodes.Cases.ServiceFolderNameTaken);
        (await TreeAsync(await SendAsync(HttpMethod.Put, $"{path}/order", admin, new ReorderServiceFoldersRequest(null, [receipts, documents]))))
            .Select(folder => folder.Name).First().ShouldBe("Ricevute");
        (await TreeAsync(await SendAsync(HttpMethod.Put, $"{path}/{receipts}", admin, new RenameServiceFolderRequest("Pagamenti"))))
            .First().Path.ShouldBe("Pagamenti");

        // The subtree goes with its root.
        (await TreeAsync(await SendAsync(HttpMethod.Delete, $"{path}/{documents}", admin))).Select(folder => folder.Name).ShouldBe(["Pagamenti"]);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/services/{Guid.NewGuid()}/folders", admin), HttpStatusCode.NotFound, EventCodes.Cases.ServiceNotFound);
    }

    private async Task<(string Admin, string Employee, string Client)> UsersAsync()
    {
        var (_, adminName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        var (_, employeeName) = await factory.AddUserAsync([TenantRole.Employee], Password);
        var (_, clientName) = await factory.AddUserAsync([TenantRole.Client], Password);
        return (await factory.SignInAsync(adminName, Password, Ct), await factory.SignInAsync(employeeName, Password, Ct), await factory.SignInAsync(clientName, Password, Ct));
    }

    private async Task<PagedResponse<ServiceResponse>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/services?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ServiceResponse>>(Ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe($"AUX-{code}");
        }
    }

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        /// <summary>An active specialization of tenant A with a unique name.</summary>
        public static async Task<Guid> AddSpecializationAsync(TenantRole role)
        {
            var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
            var specialization = Specialization.Create(
                Guid.CreateVersion7(), role, new SpecializationSpec("Spec " + Guid.NewGuid().ToString("N")[..8], null, null, null, false)).Value;
            db.Set<Specialization>().Add(specialization);
            await db.SaveChangesAsync();
            return specialization.Id;
        }
    }
}
