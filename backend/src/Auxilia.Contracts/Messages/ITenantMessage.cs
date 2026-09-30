namespace Auxilia.Contracts.Messages;

/// <summary>
/// A message handled in the context of one tenant: it travels with the <c>x-tenant-slug</c> header and is rejected
/// without it. Messages live in <c>Messages/V&lt;n&gt;/&lt;Module&gt;/</c> and go to the queue <c>auxilia.&lt;module&gt;</c>.
/// Published messages are immutable: a change is a new version (skill auxilia-messaging-rebus).
/// </summary>
public interface ITenantMessage;
