using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>
/// The tenant's password policy (F35, settings <c>auth.password.*</c>): length and character classes, history of the
/// last N passwords, expiry. Applied to activation, reset, change and passwords set by staff.
/// </summary>
public interface IPasswordPolicy
{
    Task<PasswordPolicyResponse> GetAsync(CancellationToken cancellationToken);

    /// <summary>Rules and history (when <paramref name="user"/> is given): one validation error listing every broken rule.</summary>
    Task<Result> ValidateAsync(User? user, string password, CancellationToken cancellationToken);

    Task<bool> IsExpiredAsync(User user, DateTimeOffset now, CancellationToken cancellationToken);
}

internal sealed class PasswordPolicy(ISettingsProvider settings, IPasswordHasher hasher) : IPasswordPolicy
{
    public async Task<PasswordPolicyResponse> GetAsync(CancellationToken cancellationToken) =>
        new(
            await settings.GetAsync(IdentitySettings.PasswordMinLength, cancellationToken),
            await settings.GetAsync(IdentitySettings.PasswordRequireUppercase, cancellationToken),
            await settings.GetAsync(IdentitySettings.PasswordRequireLowercase, cancellationToken),
            await settings.GetAsync(IdentitySettings.PasswordRequireDigit, cancellationToken),
            await settings.GetAsync(IdentitySettings.PasswordRequireSpecial, cancellationToken),
            await settings.GetAsync(IdentitySettings.PasswordHistoryCount, cancellationToken));

    public async Task<Result> ValidateAsync(User? user, string password, CancellationToken cancellationToken)
    {
        var policy = await GetAsync(cancellationToken);
        password ??= string.Empty;

        var broken = new List<string>();
        if (password.Length < policy.MinLength)
        {
            broken.Add("validation.password.tooShort");
        }

        if (policy.RequireUppercase && !password.Any(char.IsUpper))
        {
            broken.Add("validation.password.uppercase");
        }

        if (policy.RequireLowercase && !password.Any(char.IsLower))
        {
            broken.Add("validation.password.lowercase");
        }

        if (policy.RequireDigit && !password.Any(char.IsDigit))
        {
            broken.Add("validation.password.digit");
        }

        if (policy.RequireSpecial && password.All(char.IsLetterOrDigit))
        {
            broken.Add("validation.password.special");
        }

        if (broken.Count > 0)
        {
            return Errors.Identity.PasswordPolicyViolated(broken);
        }

        // History: legacy BCrypt hashes are verified too (migrated history, F35).
        if (user is not null && policy.HistoryCount > 0
            && user.PasswordHistory.Take(policy.HistoryCount).Any(entry => hasher.Verify(entry.PasswordHash, entry.Format, password) != PasswordVerification.Failed))
        {
            return Errors.Identity.PasswordReused(policy.HistoryCount);
        }

        return Result.Success();
    }

    public async Task<bool> IsExpiredAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!await settings.GetAsync(IdentitySettings.PasswordExpiryEnabled, cancellationToken))
        {
            return false;
        }

        var months = await settings.GetAsync(IdentitySettings.PasswordExpiryMonths, cancellationToken);
        return user.PasswordChangedAt is { } changed ? changed.AddMonths(months) <= now : user.PasswordHash is not null;
    }
}
