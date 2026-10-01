using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Directory;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Tests.Directory;

/// <summary>In-memory specializations and users shared by every unit of work of a test.</summary>
internal sealed class InMemorySpecializationData : ISpecializationDataFactory, ISpecializationData
{
    public List<Specialization> Specializations { get; } = [];

    public List<(DirectoryUser User, TenantRole Role)> Users { get; } = [];

    public int Saves { get; private set; }

    public Task<ISpecializationData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ISpecializationData>(this);

    public Task<IReadOnlyList<Specialization>> ListAsync(TenantRole? role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Specialization>>(Specializations
            .Where(item => item.IsActive && (role is null || item.Role == role))
            .OrderBy(item => item.Role.ToString(), StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());

    public Task<Specialization?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Specializations.SingleOrDefault(item => item.Id == id && item.IsActive));

    public Task<bool> NameTakenAsync(TenantRole role, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Specializations.Any(item => item.IsActive && item.Role == role && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) && item.Id != exceptId));

    public Task<IReadOnlyList<DirectoryUser>> MembersAsync(Guid id, CancellationToken cancellationToken)
    {
        var members = Specializations.Single(item => item.Id == id).Members.Select(member => member.UserId).ToHashSet();
        return Task.FromResult<IReadOnlyList<DirectoryUser>>(Users.Select(item => item.User).Where(user => members.Contains(user.UserId)).OrderBy(user => user.UserName, StringComparer.Ordinal).ToArray());
    }

    public Task<IReadOnlyList<DirectoryUser>> CandidatesAsync(Guid id, TenantRole role, string? search, int limit, CancellationToken cancellationToken)
    {
        var members = Specializations.Single(item => item.Id == id).Members.Select(member => member.UserId).ToHashSet();
        return Task.FromResult<IReadOnlyList<DirectoryUser>>(Users
            .Where(item => item.Role == role && !members.Contains(item.User.UserId))
            .Select(item => item.User)
            .Where(user => search is null || user.UserName.Contains(search, StringComparison.OrdinalIgnoreCase) || (user.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(user => user.UserName, StringComparer.Ordinal)
            .Take(limit)
            .ToArray());
    }

    public Task<IReadOnlyList<Guid>> UsersWithRoleAsync(IReadOnlyCollection<Guid> userIds, TenantRole role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Users.Where(item => item.Role == role && userIds.Contains(item.User.UserId)).Select(item => item.User.UserId).ToArray());

    public void Add(Specialization specialization) => Specializations.Add(specialization);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class SpecializationTests
{
    private InMemorySpecializationData data { get; } = new();
    private readonly SpecializationManager manager;
    private readonly SpecializationQueryService query;
    private readonly DirectoryUser anna = new(Guid.CreateVersion7(), "anna", "Anna Rossi", "anna@example.com", true);
    private readonly DirectoryUser luca = new(Guid.CreateVersion7(), "luca", "Luca Bianchi", null, false);
    private readonly DirectoryUser carla = new(Guid.CreateVersion7(), "carla", "Carla Verdi", null, true);

    public SpecializationTests()
    {
        manager = new SpecializationManager(ManagerHarness.Runner(), data, TimeProvider.System);
        query = new SpecializationQueryService(data);
        data.Users.AddRange([(anna, TenantRole.Employee), (luca, TenantRole.Employee), (carla, TenantRole.Client)]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateSpecializationRequest Create(string name = "Fisioterapia", string role = "Employee", bool isPrivate = false) =>
        new(name, role, "Area riabilitazione", "fisio@example.com", "06 123456", isPrivate);

    [Fact]
    public async Task Create_ListUpdateAndDeactivate()
    {
        var id = (await manager.CreateAsync(Create(isPrivate: true), Ct)).Value;
        (await manager.CreateAsync(Create("Nutrizione", "Client"), Ct)).IsSuccess.ShouldBeTrue();

        var list = (await query.ListAsync(null, Ct)).Value;
        list.Select(item => (item.Name, item.Role)).ShouldBe([("Nutrizione", "Client"), ("Fisioterapia", "Employee")]);
        list[1].ShouldBe(new SpecializationResponse(id, "Fisioterapia", "Employee", "Area riabilitazione", "fisio@example.com", "06 123456", true, 0));
        (await query.ListAsync("Client", Ct)).Value.ShouldHaveSingleItem().Name.ShouldBe("Nutrizione");

        (await manager.UpdateAsync(id, new("Fisioterapia sportiva", null, null, null, false), Ct)).IsSuccess.ShouldBeTrue();
        (await query.ListAsync("Employee", Ct)).Value.ShouldHaveSingleItem().ShouldBe(new SpecializationResponse(id, "Fisioterapia sportiva", "Employee", null, null, null, false, 0));

        (await manager.DeactivateAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await query.ListAsync("Employee", Ct)).Value.ShouldBeEmpty();
        (await manager.DeactivateAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNotFound);
        (await manager.UpdateAsync(id, new("X", null, null, null, false), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNotFound);

        // A deactivated specialization frees its name.
        (await manager.CreateAsync(Create("Fisioterapia sportiva"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Names_AreUniquePerRoleAmongActiveOnes()
    {
        var id = (await manager.CreateAsync(Create(), Ct)).Value;
        var other = (await manager.CreateAsync(Create("Nutrizione"), Ct)).Value;

        (await manager.CreateAsync(Create("FISIOTERAPIA"), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNameTaken);
        (await manager.CreateAsync(Create("Fisioterapia", "Client"), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.UpdateAsync(other, new("fisioterapia", null, null, null, false), Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNameTaken);
        (await manager.UpdateAsync(id, new("Fisioterapia", null, null, null, true), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("employee")]
    [InlineData("")]
    public async Task Create_OtherRoles_AreRefused(string role)
    {
        var result = await manager.CreateAsync(Create(role: role), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Directory.SpecializationInvalid);
        result.Error.ValidationErrors!.Keys.ShouldBe(["role"]);
        data.Specializations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_InvalidFields_AreRefused()
    {
        (await manager.CreateAsync(Create(name: " "), Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["name"]);
        (await query.ListAsync("Administrator", Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["role"]);
    }

    [Fact]
    public async Task Members_OnlyUsersOfTheRole()
    {
        var id = (await manager.CreateAsync(Create(), Ct)).Value;

        (await query.CandidatesAsync(id, null, Ct)).Value.Select(user => user.UserName).ShouldBe(["anna", "luca"]);
        (await manager.AddMembersAsync(id, [carla.UserId], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationMemberInvalid);
        (await manager.AddMembersAsync(id, [Guid.CreateVersion7()], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationMemberInvalid);
        (await manager.AddMembersAsync(id, [], Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["userIds"]);
        (await manager.AddMembersAsync(id, null, Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["userIds"]);

        (await manager.AddMembersAsync(id, [anna.UserId, luca.UserId, anna.UserId], Ct)).IsSuccess.ShouldBeTrue();
        (await query.MembersAsync(id, Ct)).Value.ShouldBe(
        [
            new SpecializationMemberResponse(anna.UserId, "anna", "Anna Rossi", "anna@example.com", true),
            new SpecializationMemberResponse(luca.UserId, "luca", "Luca Bianchi", null, false),
        ]);
        (await query.ListAsync(null, Ct)).Value.ShouldHaveSingleItem().MemberCount.ShouldBe(2);
        (await query.CandidatesAsync(id, null, Ct)).Value.ShouldBeEmpty();

        var saves = data.Saves;
        (await manager.AddMembersAsync(id, [anna.UserId], Ct)).IsSuccess.ShouldBeTrue();
        data.Saves.ShouldBe(saves);

        (await manager.RemoveMemberAsync(id, anna.UserId, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.RemoveMemberAsync(id, anna.UserId, Ct)).IsSuccess.ShouldBeTrue();
        (await query.MembersAsync(id, Ct)).Value.ShouldHaveSingleItem().UserName.ShouldBe("luca");
        (await query.CandidatesAsync(id, "ROSSI", Ct)).Value.ShouldHaveSingleItem().UserName.ShouldBe("anna");
    }

    [Fact]
    public async Task Members_OfAnUnknownSpecialization_AreNotFound()
    {
        var unknown = Guid.CreateVersion7();

        (await query.MembersAsync(unknown, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNotFound);
        (await query.CandidatesAsync(unknown, null, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNotFound);
        (await manager.AddMembersAsync(unknown, [anna.UserId], Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNotFound);
        (await manager.RemoveMemberAsync(unknown, anna.UserId, Ct)).Error!.Code.ShouldBe(EventCodes.Directory.SpecializationNotFound);
        (await query.CandidatesAsync(unknown, new string('a', SpecializationQueryService.SearchMaxLength + 1), Ct)).Error!.ValidationErrors!.Keys.ShouldBe(["search"]);
    }

    [Fact]
    public async Task Members_AtMostOneHundredPerRequest()
    {
        var id = (await manager.CreateAsync(Create(), Ct)).Value;

        var result = await manager.AddMembersAsync(id, Enumerable.Range(0, SpecializationManager.MaxMembersPerRequest + 1).Select(_ => Guid.CreateVersion7()).ToArray(), Ct);

        result.Error!.ValidationErrors!.Keys.ShouldBe(["userIds"]);
    }
}
