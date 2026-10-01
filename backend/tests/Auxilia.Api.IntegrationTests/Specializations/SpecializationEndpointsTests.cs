using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Host;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Contracts.Directory;
using Auxilia.Contracts.Identity;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant;
using Auxilia.SharedKernel.Tenancy;

using Npgsql;

namespace Auxilia.Api.IntegrationTests.Specializations;

/// <summary>
/// F12/F22 over HTTP: role specializations with their members and the permissions of the tenant roles, both edited by
/// the System with a tenant-scoped platform token (D-21) and refused to tenant users.
/// </summary>
public sealed class SpecializationEndpointsTests : IClassFixture<PlatformIdentityTests.Factory>
{
    private readonly PlatformIdentityTests.Factory factory;

    public SpecializationEndpointsTests(PlatformIdentityTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Specializations_AreManagedWithTheirMembers()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        var name = "Fisioterapia " + Guid.NewGuid().ToString("N")[..8];
        var (employee, employeeName) = await AddUserAsync(TenantRole.Employee);
        var (client, _) = await AddUserAsync(TenantRole.Client);

        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/specializations", token, new CreateSpecializationRequest(name, "Administrator", null, null, null, false));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe("AUX-13004");

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/specializations", token,
            new CreateSpecializationRequest(name, "Employee", "Riabilitazione", "fisio@example.test", "06 123456", true));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<CreateSpecializationResponse>(Ct))!.Id;

        using var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/specializations", token, new CreateSpecializationRequest(name.ToUpperInvariant(), "Employee", null, null, null, false));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(duplicate)).ShouldBe("AUX-13006");

        (await SendAsync(HttpMethod.Put, $"/api/v1/specializations/{id}", token, new UpdateSpecializationRequest(name, "Riabilitazione sportiva", null, null, true)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var candidates = await SendAsync(HttpMethod.Get, $"/api/v1/specializations/{id}/candidates?search={employeeName}", token);
        (await candidates.Content.ReadFromJsonAsync<SpecializationMemberResponse[]>(Ct))!.ShouldHaveSingleItem()
            .ShouldBe(new SpecializationMemberResponse(employee, employeeName, "Mario Rossi", employeeName + "@example.test", true));

        using var wrongRole = await SendAsync(HttpMethod.Post, $"/api/v1/specializations/{id}/members", token, new AddSpecializationMembersRequest([client]));
        wrongRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(wrongRole)).ShouldBe("AUX-13009");

        (await SendAsync(HttpMethod.Post, $"/api/v1/specializations/{id}/members", token, new AddSpecializationMembersRequest([employee])))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var members = await SendAsync(HttpMethod.Get, $"/api/v1/specializations/{id}/members", token);
        (await members.Content.ReadFromJsonAsync<SpecializationMemberResponse[]>(Ct))!.ShouldHaveSingleItem().UserId.ShouldBe(employee);

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/specializations?filter[role]=Employee", token);
        var listed = (await list.Content.ReadFromJsonAsync<SpecializationResponse[]>(Ct))!.Single(item => item.Id == id);
        (listed.Description, listed.Email, listed.IsPrivate, listed.MemberCount).ShouldBe(("Riabilitazione sportiva", null, true, 1));

        (await SendAsync(HttpMethod.Delete, $"/api/v1/specializations/{id}/members/{employee}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/specializations/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/specializations/{id}/members", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The deactivated specialization frees its name.
        (await SendAsync(HttpMethod.Post, "/api/v1/specializations", token, new CreateSpecializationRequest(name, "Employee", null, null, null, false)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RolePermissions_AreSetAndResetPerRole()
    {
        // Tenant B: the other test classes read the grants of tenant A.
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantB, Ct);

        using var listed = await SendAsync(HttpMethod.Get, "/api/v1/role-permissions", token);
        var permissions = (await listed.Content.ReadFromJsonAsync<RolePermissionResponse[]>(Ct))!;
        var sessions = permissions.Single(permission => permission.Code == "identity.sessions.view");
        sessions.Module.ShouldBe("identity");
        sessions.Roles.ShouldBe(["Administrator"]);
        sessions.DefaultRoles.ShouldBe(["Administrator"]);
        var clientGrants = permissions.Where(permission => permission.Roles.Contains("Client")).Select(permission => permission.Code).ToArray();

        (await SendAsync(HttpMethod.Put, "/api/v1/role-permissions/Client", token, new SetRolePermissionsRequest([.. clientGrants, "identity.sessions.view"])))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var changed = await SendAsync(HttpMethod.Get, "/api/v1/role-permissions", token);
        (await changed.Content.ReadFromJsonAsync<RolePermissionResponse[]>(Ct))!.Single(permission => permission.Code == "identity.sessions.view")
            .Roles.ShouldBe(["Administrator", "Client"]);

        using var unknown = await SendAsync(HttpMethod.Put, "/api/v1/role-permissions/Client", token, new SetRolePermissionsRequest(["identity.sessions.fly"]));
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(unknown)).ShouldBe("AUX-12058");

        (await SendAsync(HttpMethod.Delete, "/api/v1/role-permissions/Client", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var reset = await SendAsync(HttpMethod.Get, "/api/v1/role-permissions", token);
        var afterReset = (await reset.Content.ReadFromJsonAsync<RolePermissionResponse[]>(Ct))!;
        afterReset.Where(permission => permission.Roles.Contains("Client")).Select(permission => permission.Code)
            .ShouldBe(afterReset.Where(permission => permission.DefaultRoles.Contains("Client")).Select(permission => permission.Code));
    }

    [Fact]
    public async Task Editing_IsForTheSystemOnly()
    {
        foreach (var path in new[] { "/api/v1/specializations", "/api/v1/role-permissions" })
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(ApiFactory.TestTenantClaimHeader, ApiDatabase.TenantA);
            request.Headers.Add(ApiFactory.TestRolesHeader, "Administrator");
            using var response = await client.SendAsync(request, Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ErrorCodeAsync(response)).ShouldBe("AUX-12040");
        }
    }

    /// <summary>A user of tenant A with a unique user name (person Mario Rossi).</summary>
    private static async Task<(Guid UserId, string UserName)> AddUserAsync(TenantRole role)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ApiDatabase.Instance.CatalogConnectionString) { Database = "tenant_a" }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var db = new TenantDbContext(TenantDbContextOptions.Create(dataSource));
        var person = new Person(Guid.CreateVersion7(), "Mario", "Rossi", null);
        db.Set<Person>().Add(person);
        var userName = "spec-" + Guid.NewGuid().ToString("N")[..10];
        var user = User.Create(Guid.CreateVersion7(), person.Id, userName, userName + "@example.test", "it", [role], isActive: true).Value;
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(Ct);
        return (user.Id, userName);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
}
