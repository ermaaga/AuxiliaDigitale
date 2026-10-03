using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Cases;

/// <summary>
/// A category of the service catalog (<c>cases.service_categories</c>, legacy <c>MembershipType</c>, F08). The legacy app
/// had no page for it (Q26). Inactive categories stay on their services and cannot be given to others; a category can
/// be deleted only while no service uses it.
/// </summary>
public sealed class ServiceCategory : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    private ServiceCategory(Guid id)
        : base(id)
    {
        Name = string.Empty;
        IsActive = true;
    }

    private ServiceCategory()
    {
        Name = string.Empty;
    }

    /// <summary>Unique among the active categories (case-insensitive).</summary>
    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<ServiceCategory> Create(Guid id, string? name, string? description)
    {
        var category = new ServiceCategory(id);
        var applied = category.Update(name, description, isActive: true);
        return applied.IsFailure ? Result.Failure<ServiceCategory>(applied.Error!) : category;
    }

    public Result Update(string? name, string? description, bool isActive)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            return Errors.Cases.ServiceCategoryInvalid("name", "validation.serviceCategories.name");
        }

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text is { Length: > DescriptionMaxLength })
        {
            return Errors.Cases.ServiceCategoryInvalid("description", "validation.serviceCategories.description");
        }

        Name = trimmed;
        Description = text;
        IsActive = isActive;
        return Result.Success();
    }
}
