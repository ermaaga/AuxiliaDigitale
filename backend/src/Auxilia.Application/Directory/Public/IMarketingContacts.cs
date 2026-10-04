using Auxilia.Application.Abstractions.Directory;

namespace Auxilia.Application.Directory.Public;

/// <summary>A client as a marketing message sees it: name, e-mail, language and the current e-mail marketing consent (N01).</summary>
public sealed record MarketingContact(Guid PersonId, string FirstName, string LastName, string? Email, string? Language, bool EmailMarketingConsent);

/// <summary>The contacts of clients for the campaigns (Marketing, M-03); deleted clients are missing.</summary>
public interface IMarketingContacts
{
    Task<IReadOnlyList<MarketingContact>> FindAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken);
}

internal sealed class MarketingContacts(IConsentTagDataFactory data) : IMarketingContacts
{
    public async Task<IReadOnlyList<MarketingContact>> FindAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken)
    {
        if (clientIds.Count == 0)
        {
            return [];
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.MarketingContactsAsync(clientIds, cancellationToken))
            .Select(row => new MarketingContact(row.PersonId, row.FirstName, row.LastName, row.Email, row.Language, row.EmailMarketingConsent))];
    }
}
