using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Cases;
using Auxilia.Application.Directory;
using Auxilia.Application.Tests.Identity;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Directory;
using Auxilia.SharedKernel.Results;

using NSubstitute;

namespace Auxilia.Application.Tests.Imports;

public sealed class ImportTargetTests
{
    private const string FiscalCode = "RSSMRA80A01H501U";

    private readonly IImportLookups lookups = Substitute.For<IImportLookups>();
    private readonly ManualTimeProvider clock = new();

    public ImportTargetTests()
    {
        lookups.TakenFiscalCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        lookups.TakenUserNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        lookups.EmployeesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, Guid>());
        lookups.ClientsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, Guid>());
        lookups.ServicesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, Guid>());
        lookups.ServiceCategoriesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, Guid>());
        lookups.EmployeeSpecializationsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, Guid>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ImportRow Row(int number, params (string Key, string Value)[] values) =>
        new(number, values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static ImportRow Client(int number, string fiscalCode = FiscalCode, string email = "mario@example.test", string birthDate = "1980-01-01") =>
        Row(number, ("firstName", "Mario"), ("lastName", "Rossi"), ("email", email), ("fiscalCode", fiscalCode), ("birthDate", birthDate));

    private static Dictionary<string, string[]> Errors(ImportRowErrors errors) => errors.Errors.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public async Task Clients_AreCheckedWithTheStaffRules_InTheFileAndInTheTenant()
    {
        var employee = Guid.CreateVersion7();
        lookups.TakenUserNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>(["taken@example.test"], StringComparer.OrdinalIgnoreCase));
        lookups.EmployeesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["sara.gallo"] = employee });
        var clients = Substitute.For<IClientManager>();
        var target = new ClientImportTarget(clients, lookups, clock);

        var errors = await target.ValidateAsync(
            [
                Client(2),
                Client(3, email: "other@example.test"),
                Row(4, ("firstName", "Anna"), ("email", "taken@example.test"), ("fiscalCode", "XX"), ("birthDate", "31/02/1990"), ("employee", "nobody")),
                Client(5, fiscalCode: "BNCNNA90A41H501X", email: "anna@example.test", birthDate: "01/01/1990"),
            ],
            Ct);

        errors[0].IsValid.ShouldBeTrue();
        Errors(errors[1])["fiscalCode"].ShouldBe([ImportValues.Duplicate]);
        var third = Errors(errors[2]);
        third["lastName"].ShouldContain(ImportValues.Required);
        third["birthDate"].ShouldContain(ImportValues.InvalidDate);
        third["email"].ShouldBe([ImportValues.Taken]);
        third["employee"].ShouldBe([ImportValues.NotFound]);
        third.ShouldContainKey("fiscalCode");
        errors[3].IsValid.ShouldBeTrue();

        clients.CreateAsync(Arg.Any<CreateClientRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new CreateClientResponse(Guid.CreateVersion7(), true, null)));
        var row = Row(2, ("firstName", "Mario"), ("lastName", "Rossi"), ("email", "m@example.test"), ("fiscalCode", FiscalCode), ("birthDate", "1980-01-01"), ("employee", "SARA.GALLO"));
        (await target.ImportAsync(row, Ct)).IsSuccess.ShouldBeTrue();
        await clients.Received(1).CreateAsync(
            Arg.Is<CreateClientRequest>(request => request.FiscalCode == FiscalCode && request.BirthDate == new DateOnly(1980, 1, 1) && request.EmployeeUserId == employee),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Employees_NeedNameAndEmail_AndSignInDefaultsToYes()
    {
        var employees = Substitute.For<IEmployeeManager>();
        employees.CreateAsync(Arg.Any<CreateEmployeeRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new CreateEmployeeResponse(Guid.CreateVersion7(), true, null)));
        lookups.TakenFiscalCodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>([FiscalCode], StringComparer.OrdinalIgnoreCase));
        var target = new EmployeeImportTarget(employees, lookups, clock);

        var errors = await target.ValidateAsync(
            [
                Row(2, ("firstName", "Sara"), ("lastName", "Gallo"), ("email", "sara@example.test"), ("birthDate", "1990-05-01")),
                Row(3, ("firstName", "Luca"), ("lastName", "Neri"), ("email", "luca@example.test"), ("birthDate", "1990-05-01"), ("fiscalCode", FiscalCode), ("canSignIn", "forse")),
            ],
            Ct);

        errors[0].IsValid.ShouldBeTrue();
        Errors(errors[1]).ShouldSatisfyAllConditions(
            second => second["fiscalCode"].ShouldBe([ImportValues.Taken]), second => second["canSignIn"].ShouldBe([ImportValues.InvalidBoolean]));

        await target.ImportAsync(Row(2, ("firstName", "Sara"), ("lastName", "Gallo"), ("email", "sara@example.test")), Ct);
        await target.ImportAsync(Row(3, ("firstName", "Ugo"), ("lastName", "Bo"), ("email", "ugo@example.test"), ("canSignIn", "no")), Ct);
        await employees.Received(1).CreateAsync(Arg.Is<CreateEmployeeRequest>(request => request.Email == "sara@example.test" && request.CanSignIn), Arg.Any<CancellationToken>());
        await employees.Received(1).CreateAsync(Arg.Is<CreateEmployeeRequest>(request => request.Email == "ugo@example.test" && !request.CanSignIn), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Services_CheckNumbersNamesAndReferences()
    {
        var category = Guid.CreateVersion7();
        lookups.ServicesAsync(Arg.Any<IReadOnlyCollection<string>>(), false, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["ISEE"] = Guid.CreateVersion7() });
        lookups.ServiceCategoriesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["Fiscale"] = category });
        var catalog = Substitute.For<IServiceCatalogManager>();
        catalog.CreateServiceAsync(Arg.Any<CreateServiceRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.CreateVersion7()));
        var target = new ServiceImportTarget(catalog, lookups);

        var errors = await target.ValidateAsync(
            [
                Row(2, ("name", "730"), ("price", "1.234,50"), ("durationDays", "30"), ("category", "fiscale")),
                Row(3, ("name", "isee"), ("price", "abc"), ("durationDays", "1.5"), ("specialization", "Nessuna")),
                Row(4, ("name", "730"), ("price", "-1"), ("durationDays", "0")),
            ],
            Ct);

        errors[0].IsValid.ShouldBeTrue();
        Errors(errors[1]).ShouldSatisfyAllConditions(
            second => second["name"].ShouldBe([ImportValues.Taken]),
            second => second["price"].ShouldBe([ImportValues.InvalidNumber]),
            second => second["durationDays"].ShouldBe([ImportValues.InvalidNumber]),
            second => second["specialization"].ShouldBe([ImportValues.NotFound]));
        Errors(errors[2]).ShouldSatisfyAllConditions(
            third => third["name"].ShouldBe([ImportValues.Duplicate]),
            third => third["price"].ShouldBe(["validation.services.price"]),
            third => third["durationDays"].ShouldBe(["validation.services.durationDays"]));

        await target.ImportAsync(Row(2, ("name", "730"), ("price", "1.234,50"), ("durationDays", "30"), ("category", "fiscale")), Ct);
        await catalog.Received(1).CreateServiceAsync(
            Arg.Is<CreateServiceRequest>(request => request.Name == "730" && request.Price == 1234.50m && request.DurationDays == 30 && request.CategoryId == category),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cases_FindTheClientByFiscalCode_AndTheActiveServiceByName()
    {
        lookups.ClientsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { [FiscalCode] = Guid.CreateVersion7() });
        lookups.ServicesAsync(Arg.Any<IReadOnlyCollection<string>>(), true, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["730"] = Guid.CreateVersion7() });
        var target = new CaseImportTarget(null!, lookups);

        var errors = await target.ValidateAsync(
            [
                Row(2, ("clientFiscalCode", FiscalCode.ToLowerInvariant()), ("service", "730"), ("startedOn", "2026-01-10"), ("dueOn", "2026-02-10")),
                Row(3, ("clientFiscalCode", "NOBODY"), ("service", "ISEE"), ("startedOn", "2026-03-01"), ("dueOn", "2026-02-01")),
                Row(4, ("service", "730")),
            ],
            Ct);

        errors[0].IsValid.ShouldBeTrue();
        Errors(errors[1]).ShouldSatisfyAllConditions(
            second => second["clientFiscalCode"].ShouldBe([ImportValues.NotFound]),
            second => second["service"].ShouldBe([ImportValues.NotFound]),
            second => second["dueOn"].ShouldBe(["validation.cases.dueOn"]));
        Errors(errors[2])["clientFiscalCode"].ShouldBe([ImportValues.Required]);
    }

    [Theory]
    [InlineData("2026-03-04", 2026, 3, 4)]
    [InlineData("04/03/2026", 2026, 3, 4)]
    [InlineData("4/3/2026", 2026, 3, 4)]
    [InlineData("2026-03-04T00:00", 2026, 3, 4)]
    public void Dates_AcceptIsoAndItalianFormats(string text, int year, int month, int day)
    {
        var errors = new ImportRowErrors();
        ImportValues.Date(Row(2, ("d", text)), "d", errors).ShouldBe(new DateOnly(year, month, day));
        errors.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("1.234,56", 1234.56)]
    public void Numbers_AcceptDotOrComma(string text, double expected)
    {
        ImportValues.Amount(Row(2, ("n", text)), "n", new ImportRowErrors()).ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("sì", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("No", false)]
    public void Booleans_AcceptItalianAndEnglishWords(string text, bool expected)
    {
        ImportValues.Boolean(Row(2, ("b", text)), "b", new ImportRowErrors()).ShouldBe(expected);
    }

    [Fact]
    public void ManagerErrors_BecomeRowErrors()
    {
        var errors = new ImportRowErrors();
        errors.Add(Diagnostics.Errors.Directory.FiscalCodeTaken());
        errors.Add(Diagnostics.Errors.Cases.CaseInvalid("dueOn", "validation.cases.dueOn"));

        Errors(errors).ShouldSatisfyAllConditions(
            all => all["row"].ShouldBe([$"errors.AUX-{Diagnostics.EventCodes.Directory.FiscalCodeTaken}"]),
            all => all["dueOn"].ShouldBe(["validation.cases.dueOn"]));
    }
}
