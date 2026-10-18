using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Mailbox;

/// <summary>Permissions of the mailbox module (N05): each user works only on their own accounts.</summary>
public static class MailboxPermissions
{
    /// <summary>Connect, change and disconnect one's own Google, Microsoft and PEC / IMAP accounts.</summary>
    public const string ConnectAccounts = "mailbox.accounts.connect";

    /// <summary>Read the mail and the calendar events of one's own connected accounts.</summary>
    public const string ViewMail = "mailbox.mail.view";

    /// <summary>File a message of one's own mailbox into a case (F10 rules of the case apply).</summary>
    public const string FileMail = "mailbox.mail.file";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ConnectAccounts, [TenantRole.Administrator, TenantRole.Employee]),
        new(ViewMail, [TenantRole.Administrator, TenantRole.Employee]),
        new(FileMail, [TenantRole.Administrator, TenantRole.Employee]),
    ];
}
