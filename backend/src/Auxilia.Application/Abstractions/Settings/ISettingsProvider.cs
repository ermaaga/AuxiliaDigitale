namespace Auxilia.Application.Abstractions.Settings;

/// <summary>
/// Effective setting values for the current scope (tenant in <c>ITenantContext</c>, user in <c>ICurrentUser</c>):
/// user → tenant → platform → code default, limited to the levels each definition allows. Platform and tenant values
/// come from one cached snapshot per tenant (<c>t:{slug}:configuration:snapshot</c>); a stored value that no longer
/// matches its definition is ignored with a warning and the next level applies.
/// </summary>
public interface ISettingsProvider
{
    Task<T> GetAsync<T>(SettingDefinition<T> definition, CancellationToken cancellationToken)
        where T : notnull;

    /// <summary>The decrypted secret, or <c>null</c> when no level stores it. Call it where the secret is used, never cache the result.</summary>
    Task<string?> GetSecretAsync(SecretSettingDefinition definition, CancellationToken cancellationToken);
}

/// <summary>All registered definitions, by key (unique across modules).</summary>
public interface ISettingDefinitionRegistry
{
    IReadOnlyCollection<SettingDefinition> All { get; }

    SettingDefinition? Find(string key);
}

/// <summary>Protects secret setting values at rest and in the cache (Data Protection).</summary>
public interface ISettingSecretProtector
{
    string Protect(string plainValue);

    string Unprotect(string protectedValue);
}
