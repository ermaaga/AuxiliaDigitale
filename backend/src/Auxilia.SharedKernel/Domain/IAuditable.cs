namespace Auxilia.SharedKernel.Domain;

/// <summary>
/// Business data whose changes are recorded: audit columns (<c>created_at/by</c>, <c>updated_at/by</c>) and one row in
/// <c>audit.entity_changes</c> per insert/update/delete, with the actor type (User/Platform/System). Filled by the
/// persistence layer; the domain does not handle these values.
/// </summary>
public interface IAuditable;

/// <summary>
/// Data that users "delete" but must be kept (people, cases, documents, requests, tasks): deleting it sets
/// <c>is_deleted</c>, <c>deleted_at</c>, <c>deleted_by</c> and hides it from every query (global filter).
/// </summary>
public interface ISoftDeletable;
