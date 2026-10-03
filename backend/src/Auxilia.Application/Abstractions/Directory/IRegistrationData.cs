using Auxilia.Domain.Directory;

namespace Auxilia.Application.Abstractions.Directory;

/// <summary>Sortable columns of the registration list.</summary>
public enum RegistrationSort
{
    RequestedAt,
    ProcessedAt,
    LastName,
}

/// <summary>A page request of the registration list, already validated; <see cref="Search"/> matches anywhere, case-insensitive.</summary>
public sealed record RegistrationFilter(
    RegistrationStatus? Status,
    DateTimeOffset? From,
    DateTimeOffset? Before,
    string? Search,
    RegistrationSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>A registration request with the name of the staff user who processed it.</summary>
public sealed record RegistrationRow(RegistrationRequest Request, string? ProcessedByName);

/// <summary>
/// Registration requests of the current tenant (F02/F03), one unit of work (inside a write operation it joins the
/// operation's transaction).
/// </summary>
public interface IRegistrationData : IAsyncDisposable
{
    Task<(IReadOnlyList<RegistrationRow> Items, int Total)> PageAsync(RegistrationFilter filter, CancellationToken cancellationToken);

    /// <summary>The request (untracked) with the processor's name; <c>null</c> when it does not exist.</summary>
    Task<RegistrationRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The request (tracked).</summary>
    Task<RegistrationRequest?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A pending request has the e-mail (already normalised).</summary>
    Task<bool> PendingExistsAsync(string email, CancellationToken cancellationToken);

    /// <summary>A person not deleted or a user has the e-mail, or a user has it as user name (case-insensitive).</summary>
    Task<bool> EmailRegisteredAsync(string email, CancellationToken cancellationToken);

    void Add(RegistrationRequest request);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IRegistrationDataFactory
{
    Task<IRegistrationData> OpenAsync(CancellationToken cancellationToken);
}
