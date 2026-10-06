using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-02 (F35, only with the legacy Security_Update schema): <c>PasswordHistories</c> → <c>identity.password_history</c>
/// (BCrypt) and <c>LoginAuditLogs</c> → <c>identity.login_attempts</c>.
/// </summary>
internal sealed class AccountSecurityStep : ILegacyImportStep
{
    public const string HistoryTable = "PasswordHistories";
    public const string AttemptsTable = "LoginAuditLogs";

    public string Name => "account security";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Legacy.Variant.SecurityUpdate)
        {
            return;
        }

        var users = await context.Tenant.Set<User>().ToDictionaryAsync(user => user.Id, cancellationToken);
        var history = context.Report.For(HistoryTable);
        foreach (var legacy in await context.Legacy.PasswordHistories.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (context.Ids.Find(UsersStep.Table, legacy.UserId) is not { } userId || !users.TryGetValue(userId, out var user))
            {
                history.Excluded++;
                continue;
            }

            if (user.ImportLegacyPasswordHistory(legacy.PasswordHash, LegacyImportContext.Instant(legacy.CreatedAt)))
            {
                history.Created++;
            }
        }

        var attempts = context.Report.For(AttemptsTable);
        foreach (var legacy in await context.Legacy.LoginAuditLogs.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (context.Ids.Find(AttemptsTable, legacy.Id) is not null)
            {
                continue;
            }

            if (Method(legacy.LoginType) is not { } method)
            {
                context.Report.Skip(AttemptsTable, legacy.Id, "unknown sign-in method");
                continue;
            }

            var userId = legacy.UserId is { } legacyUser ? context.Ids.Find(UsersStep.Table, legacyUser) : null;
            var id = context.Ids.NewId();
            context.Tenant.Add(new LoginAttempt(
                id, userId, legacy.Username, method, LegacyImportContext.Instant(legacy.LoginAt), legacy.Success, legacy.FailureReason, legacy.IpAddress,
                userAgent: null));
            context.Ids.Add(AttemptsTable, legacy.Id, id);
            attempts.Created++;
        }
    }

    /// <summary>Legacy <c>LoginType</c> → method code of <c>IAuthenticationMethod</c>.</summary>
    public static string? Method(string loginType) => loginType switch
    {
        "Password" => "password",
        "Otp" => "email-otp",
        _ => null,
    };
}
