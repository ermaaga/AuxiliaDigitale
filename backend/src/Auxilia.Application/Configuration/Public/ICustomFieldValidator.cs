using System.Text.Json;

using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration.Public;

/// <summary>
/// Server-side validation of the custom field values of a record (F20), for the modules that store them
/// (<c>custom_fields jsonb</c>): only defined keys, the right type, options of the selects, required fields present.
/// </summary>
public interface ICustomFieldValidator
{
    /// <param name="entityType">An entity declared by a module (<c>client</c>, <c>case</c>…).</param>
    /// <param name="values">A JSON object, or null/undefined for none.</param>
    /// <returns>
    /// The values as a normalised JSON object (defined keys only, texts trimmed, empty values dropped), or
    /// <c>AUX-20018</c> with one error per field (<c>customFields.&lt;key&gt;</c>).
    /// </returns>
    Task<Result<string>> ValidateAsync(string entityType, JsonElement? values, CancellationToken cancellationToken);
}
