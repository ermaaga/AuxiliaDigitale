using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;

namespace Auxilia.Domain.Tests.Cases;

public sealed class ServiceCatalogTests
{
    private static ServiceDetails Details(string? name = " ISEE ", decimal price = 10m, int duration = 1, bool active = true) =>
        new(name, "  Indicatore  ", price, duration, Guid.CreateVersion7(), Guid.CreateVersion7(), active);

    [Fact]
    public void Service_Create_TrimsAndKeepsCategoryAndSpecialization()
    {
        var details = Details();
        var service = Service.Create(Guid.CreateVersion7(), details).Value;

        (service.Name, service.Description, service.Price, service.Currency, service.DurationDays, service.IsActive)
            .ShouldBe(("ISEE", "Indicatore", 10m, "EUR", 1, true));
        (service.CategoryId, service.SpecializationId).ShouldBe((details.CategoryId, details.SpecializationId));
    }

    [Fact]
    public void Service_InvalidValues_AreReportedTogether_AndLeaveItUntouched()
    {
        var service = Service.Create(Guid.CreateVersion7(), Details()).Value;

        var result = service.Update(new ServiceDetails(" ", new string('x', Service.DescriptionMaxLength + 1), 10.005m, 0, null, null, false));

        result.Error!.Code.ShouldBe(EventCodes.Cases.ServiceInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["name", "description", "price", "durationDays"], ignoreOrder: true);
        (service.Name, service.IsActive).ShouldBe(("ISEE", true));
    }

    [Theory]
    [InlineData(-0.01, 1)]
    [InlineData(10_000_000_000, 1)]
    [InlineData(0, Service.MaxDurationDays + 1)]
    public void Service_PriceAndDuration_HaveLimits(double price, int duration)
    {
        Service.Create(Guid.CreateVersion7(), Details(price: (decimal)price, duration: duration)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Service_Update_CanDeactivateAndClearReferences()
    {
        var service = Service.Create(Guid.CreateVersion7(), Details()).Value;

        service.Update(new ServiceDetails("ISEE 2026", null, 0m, 365, null, null, false)).IsSuccess.ShouldBeTrue();

        (service.Name, service.Description, service.Price, service.IsActive, service.CategoryId, service.SpecializationId)
            .ShouldBe(("ISEE 2026", (string?)null, 0m, false, (Guid?)null, (Guid?)null));
    }

    [Fact]
    public void Category_CreateUpdateAndValidation()
    {
        var category = ServiceCategory.Create(Guid.CreateVersion7(), " Fiscale ", " ").Value;
        (category.Name, category.Description, category.IsActive).ShouldBe(("Fiscale", (string?)null, true));

        category.Update("Fisco", "Pratiche fiscali", isActive: false).IsSuccess.ShouldBeTrue();
        (category.Name, category.IsActive).ShouldBe(("Fisco", false));

        ServiceCategory.Create(Guid.CreateVersion7(), "", null).Error!.ValidationErrors.Keys.ShouldBe(["name"]);
        category.Update("Fisco", new string('x', ServiceCategory.DescriptionMaxLength + 1), true).Error!.ValidationErrors.Keys.ShouldBe(["description"]);
        category.IsActive.ShouldBeFalse();
    }
}

public sealed class ServiceFolderDomainTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    public void Name_WithoutPathSeparators_AndNotEmpty(string name)
    {
        ServiceFolder.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), null, name, 0).Error!.ValidationErrors.Keys.ShouldBe(["name"]);
    }

    [Fact]
    public void Rename_AndMove()
    {
        var folder = ServiceFolder.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), null, " Documenti ", 2).Value;

        folder.Rename(new string('x', ServiceFolder.NameMaxLength + 1)).IsFailure.ShouldBeTrue();
        folder.Rename("Archivio").IsSuccess.ShouldBeTrue();
        folder.MoveTo(0);

        (folder.Name, folder.SortOrder, folder.ParentId).ShouldBe(("Archivio", 0, (Guid?)null));
    }
}
