using System.Text.Json;

using Auxilia.Application.Abstractions.Channels;
using Auxilia.Domain.Messaging;
using Auxilia.Infrastructure.Adapters.Channels.Smtp;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (N03, F23): <c>EmailConfigurations</c> → an <c>smtp</c> account in <c>configuration.messaging_accounts</c>, the
/// password encrypted with Data Protection (never logged); the active legacy account becomes the default e-mail account
/// when the tenant has none (no sender rules: the default account serves every purpose and role).
/// </summary>
internal sealed class MessagingAccountStep : ILegacyImportStep
{
    public const string Table = "EmailConfigurations";

    private readonly IAccountSecretProtector secrets;

    /// <summary>Provider code of the SMTP adapter (<c>SmtpEmailChannel.ProviderKey</c>).</summary>
    public const string SmtpProvider = "smtp";

    public MessagingAccountStep(IAccountSecretProtector secrets) => this.secrets = secrets;

    public string Name => "sending accounts";

    /// <summary>Legacy <c>EnableSsl</c> + port → <see cref="SmtpSecurity"/> (465 is SSL on connect, as MailKit expects).</summary>
    public static SmtpSecurity Security(bool enableSsl, int port) =>
        port == 465 ? SmtpSecurity.SslOnConnect : enableSsl ? SmtpSecurity.StartTls : SmtpSecurity.None;

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var accounts = await context.Tenant.Set<MessagingAccount>().ToListAsync(cancellationToken);
        var result = context.Report.For(Table);
        foreach (var legacy in await context.Legacy.EmailConfigurations.OrderByDescending(row => row.IsActive).ThenBy(row => row.Id).ToListAsync(cancellationToken))
        {
            var settings = JsonSerializer.Serialize(
                new SmtpSettings(legacy.SmtpServer.Trim(), legacy.SmtpPort, Security(legacy.EnableSsl, legacy.SmtpPort), Text(legacy.Username), legacy.FromEmail.Trim(), Text(legacy.FromName)),
                SmtpSettings.Json);
            var secret = string.IsNullOrEmpty(legacy.Password) ? null : secrets.Protect(legacy.Password);
            var name = LegacyText.Fit(string.IsNullOrWhiteSpace(legacy.FromName) ? $"SMTP {legacy.SmtpServer}" : legacy.FromName, MessagingAccount.NameMaxLength, out _);

            if (context.Ids.Find(Table, legacy.Id) is { } id && accounts.FirstOrDefault(account => account.Id == id) is { } current)
            {
                if (current.Update(name, settings, secret).IsSuccess)
                {
                    result.Updated++;
                }

                continue;
            }

            var created = MessagingAccount.Create(context.Ids.NewId(), MessageChannel.Email, SmtpProvider, name, settings, secret);
            if (created.IsFailure)
            {
                context.Report.Skip(Table, legacy.Id, "the account is not valid");
                continue;
            }

            var account = created.Value;
            account.SetActive(legacy.IsActive);
            if (legacy.IsActive && !accounts.Any(other => other.Channel == MessageChannel.Email && other.IsDefault))
            {
                account.SetDefault(true);
            }
            else if (legacy.IsActive)
            {
                context.Report.Warn(Table, legacy.Id, "the tenant already has a default e-mail account: imported as not default");
            }

            context.Tenant.Add(account);
            accounts.Add(account);
            context.Ids.Add(Table, legacy.Id, account.Id);
            result.Created++;
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
