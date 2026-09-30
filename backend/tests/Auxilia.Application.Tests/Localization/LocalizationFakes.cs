using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Localization;
using Auxilia.Application.Tests.Configuration;
using Auxilia.Domain.Localization;
using Auxilia.Domain.Platform;


using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Localization;

/// <summary>In-memory languages and keys, both the unit of work of the Manager and the reader of the queries.</summary>
internal sealed class InMemoryLocalizationData : ILocalizationDataFactory, ILocalizationData, ILocalizationReader
{
    public List<Language> Languages { get; } = [];

    public List<ResourceKey> Keys { get; } = [];

    public int Saves { get; private set; }

    public int BundleLoads { get; private set; }

    public Task<ILocalizationData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ILocalizationData>(this);

    Task<IReadOnlyList<Language>> ILocalizationData.LanguagesAsync(CancellationToken cancellationToken) => LanguagesAsync(cancellationToken);

    public Task<IReadOnlyList<Language>> LanguagesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Language>>(Languages.OrderBy(language => language.Code, StringComparer.Ordinal).ToList());

    public Task<ResourceKey?> FindKeyAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Keys.SingleOrDefault(key => key.Id == id));

    public Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Keys.Any(item => item.Key == key));

    public void Add(ResourceKey key) => Keys.Add(key);

    public void Remove(ResourceKey key) => Keys.Remove(key);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<BundleSource> BundleSourceAsync(IReadOnlyCollection<string> languageCodes, CancellationToken cancellationToken)
    {
        BundleLoads++;
        return Task.FromResult(new BundleSource(
            Keys.Select(key => key.Key).ToArray(),
            Keys.SelectMany(key => key.Translations.Where(translation => languageCodes.Contains(translation.LanguageCode))
                .Select(translation => new BundleTranslation(key.Key, translation.LanguageCode, translation.Value))).ToArray()));
    }

    public Task<(IReadOnlyList<ResourceKey> Items, long TotalCount)> ListKeysAsync(ResourceKeyQuery query, CancellationToken cancellationToken)
    {
        var items = Keys.Where(key => query.Category is null || key.Category == query.Category)
            .Where(key => query.MissingLanguage is null || key.Translation(query.MissingLanguage) is null)
            .Where(key => query.Search is null || key.Key.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || key.Translations.Any(translation => translation.Value.Contains(query.Search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(key => key.Key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<(IReadOnlyList<ResourceKey>, long)>((items.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), items.Count));
    }

    public Task<ResourceKey?> GetKeyAsync(Guid id, CancellationToken cancellationToken) => FindKeyAsync(id, cancellationToken);

    public Task<IReadOnlyDictionary<string, int>> CategoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(Keys.GroupBy(key => key.Category).ToDictionary(group => group.Key, group => group.Count()));

    public Task<(int KeyCount, IReadOnlyDictionary<string, int> TranslatedByLanguage)> CoverageAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(int, IReadOnlyDictionary<string, int>)>((Keys.Count,
            Keys.SelectMany(key => key.Translations).GroupBy(translation => translation.LanguageCode).ToDictionary(group => group.Key, group => group.Count())));

    public ResourceKey AddKey(string name, string category = "common", string? en = null, string? it = null)
    {
        var key = ResourceKey.Create(Guid.CreateVersion7(), name, category, null, isSystem: true).Value;
        if (en is not null)
        {
            key.UpgradeSystemTranslation("en", en);
        }

        if (it is not null)
        {
            key.UpgradeSystemTranslation("it", it);
        }

        Keys.Add(key);
        return key;
    }
}

/// <summary>The localization services over in-memory data for tenant <c>acme</c> (default language it).</summary>
internal sealed class LocalizationHarness
{
    public static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    public LocalizationHarness(TenantInfo? tenant = null)
    {
        tenant ??= Acme;
        TenantContext.Current.Returns(tenant);
        TenantContext.Tenant.Returns(tenant);
        Data.Languages.Add(new Language(Guid.CreateVersion7(), "en", "English"));
        Data.Languages.Add(new Language(Guid.CreateVersion7(), "it", "Italiano"));

        Languages = new LanguagesCache(Cache, TenantContext, Data);
        Bundles = new TranslationBundleCache(Cache, TenantContext, Data);
        Queries = new LocalizationQueryService(Languages, Bundles, Data, TenantContext);
        Keys = new ResourceKeyManager(Platform.ManagerHarness.Runner(), Data, TenantContext, Cache);
        Localizer = new Localizer(Languages, Bundles, TenantContext, NullLogger<Localizer>.Instance);
    }

    public InMemoryLocalizationData Data { get; } = new();

    public FakeReferenceDataCache Cache { get; } = new();

    public ITenantContext TenantContext { get; } = Substitute.For<ITenantContext>();

    public LanguagesCache Languages { get; }

    public TranslationBundleCache Bundles { get; }

    public LocalizationQueryService Queries { get; }

    public ResourceKeyManager Keys { get; }

    public Localizer Localizer { get; }
}
