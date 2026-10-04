namespace Auxilia.Contracts.Directory;

/// <summary>A tag (N01) with the number of clients that have it.</summary>
public sealed record TagResponse(Guid Id, string Name, string? Color, int ClientCount);

/// <param name="Color">A badge colour <c>#rrggbb</c>, or none.</param>
public sealed record SaveTagRequest(string Name, string? Color);

public sealed record CreateTagResponse(Guid Id);

/// <summary>A tag of a client.</summary>
public sealed record ClientTagResponse(Guid Id, string Name, string? Color);

/// <summary>The whole set of tags of a client.</summary>
public sealed record SetClientTagsRequest(IReadOnlyList<Guid> TagIds);

/// <summary>Adds and removes tags on many clients at once (selection of the clients table), at most 500 clients.</summary>
public sealed record BulkClientTagsRequest(IReadOnlyList<Guid> ClientIds, IReadOnlyList<Guid>? Add, IReadOnlyList<Guid>? Remove);

/// <param name="Changed">Assignments added plus removed.</param>
public sealed record BulkClientTagsResponse(int Changed);

/// <summary>A consent change recorded by staff (source <c>Staff</c>).</summary>
/// <param name="Purpose"><c>Marketing</c> or <c>Privacy</c>.</param>
/// <param name="Channel"><c>Email</c> or <c>WhatsApp</c>.</param>
/// <param name="Version">The version of the consent text, when known.</param>
public sealed record RecordConsentRequest(string Purpose, string Channel, bool Granted, string? Version, string? Note);

/// <summary>The current consent for a purpose and a channel: the latest change, or not granted when none.</summary>
/// <param name="Source"><c>Staff</c>, <c>Import</c>, <c>Api</c> or <c>LegacyMigration</c>; none when never recorded.</param>
public sealed record ConsentStateResponse(string Purpose, string Channel, bool Granted, DateTimeOffset? Since, string? Source);

public sealed record ConsentChangeResponse(
    Guid Id, string Purpose, string Channel, bool Granted, string Source, string? Version, string? Note, DateTimeOffset RecordedAt, string? RecordedBy);

/// <summary>The consents of a client (N01): current state per purpose and channel, then every change, newest first.</summary>
public sealed record ClientConsentsResponse(IReadOnlyList<ConsentStateResponse> Current, IReadOnlyList<ConsentChangeResponse> History);
