namespace Auxilia.Application.Localization.Public;

/// <summary>
/// Server-side texts (e-mails, PDF, notifications) in the recipient's language (skill auxilia-localization), from the
/// same cached bundles the clients get: requested language → tenant default → English → the key (logged as missing).
/// </summary>
public interface ILocalizer
{
    /// <param name="languageCode">The recipient's language; an unknown or inactive one uses the tenant default.</param>
    /// <param name="arguments">Values of simple <c>{name}</c> placeholders (plurals and selects are not interpreted).</param>
    Task<string> GetAsync(string key, string? languageCode, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken);
}
