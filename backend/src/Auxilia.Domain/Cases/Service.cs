using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Cases;

/// <summary>What staff enter for a service; the category and the specialization are checked by the manager.</summary>
public sealed record ServiceDetails(
    string? Name,
    string? Description,
    decimal Price,
    int DurationDays,
    Guid? CategoryId,
    Guid? SpecializationId,
    bool IsActive);

/// <summary>
/// A service of the catalog (<c>cases.services</c>, legacy <c>Membership</c>, F08): what a case is opened for. The price
/// is a snapshot source for new cases (Q02), in euro with two decimals; the duration gives a case its end date. The
/// optional specialization (Employee role) makes the cases private when it is private (F10). An inactive service stays
/// on its cases and cannot be chosen for new ones; deleting is soft (Q28) and only for services without cases.
/// </summary>
public sealed class Service : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxDurationDays = 3650;
    public const string DefaultCurrency = "EUR";

    /// <summary>Fits <c>numeric(12,2)</c>.</summary>
    public const decimal MaxPrice = 9_999_999_999.99m;

    private Service(Guid id)
        : base(id)
    {
        Name = string.Empty;
        Currency = DefaultCurrency;
    }

    private Service()
    {
        Name = Currency = string.Empty;
    }

    /// <summary>Unique among the services not deleted (case-insensitive).</summary>
    public string Name { get; private set; }

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>ISO 4217; euro for every service today.</summary>
    public string Currency { get; private set; }

    public int DurationDays { get; private set; }

    public Guid? CategoryId { get; private set; }

    /// <summary>An Employee specialization (Directory, F12).</summary>
    public Guid? SpecializationId { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Service> Create(Guid id, ServiceDetails details)
    {
        var service = new Service(id);
        var applied = service.Update(details);
        return applied.IsFailure ? Result.Failure<Service>(applied.Error!) : service;
    }

    /// <summary>Checks every value (all errors at once, one per field) and replaces them.</summary>
    public Result Update(ServiceDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = details.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > NameMaxLength)
        {
            errors["name"] = ["validation.services.name"];
        }

        var description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        if (description is { Length: > DescriptionMaxLength })
        {
            errors["description"] = ["validation.services.description"];
        }

        if (details.Price is < 0 or > MaxPrice || decimal.Round(details.Price, 2) != details.Price)
        {
            errors["price"] = ["validation.services.price"];
        }

        if (details.DurationDays is < 1 or > MaxDurationDays)
        {
            errors["durationDays"] = ["validation.services.durationDays"];
        }

        if (errors.Count > 0)
        {
            return Errors.Cases.ServiceInvalid(errors);
        }

        Name = name;
        Description = description;
        Price = details.Price;
        DurationDays = details.DurationDays;
        CategoryId = details.CategoryId;
        SpecializationId = details.SpecializationId;
        IsActive = details.IsActive;
        return Result.Success();
    }
}
