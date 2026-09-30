using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Identity;

internal sealed class LoginAttemptReader(ITenantDbContextFactory databases) : ILoginAttemptReader
{
    public async Task<(IReadOnlyList<LoginAttempt> Items, long TotalCount)> ListAsync(LoginAttemptQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await databases.CreateAsync(cancellationToken);
        var attempts = db.Set<LoginAttempt>().AsNoTracking();

        // user_name is citext: "contains" is case-insensitive in the database.
        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            var pattern = "%" + query.UserName.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            attempts = attempts.Where(attempt => EF.Functions.Like(attempt.UserName, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.Method))
        {
            attempts = attempts.Where(attempt => attempt.Method == query.Method);
        }

        if (query.Succeeded is { } succeeded)
        {
            attempts = attempts.Where(attempt => attempt.Succeeded == succeeded);
        }

        if (query.From is { } from)
        {
            attempts = attempts.Where(attempt => attempt.AttemptedAt >= from);
        }

        if (query.To is { } to)
        {
            attempts = attempts.Where(attempt => attempt.AttemptedAt <= to);
        }

        var total = await attempts.LongCountAsync(cancellationToken);
        var sorted = query.Sort switch
        {
            "attemptedAt" => attempts.OrderBy(attempt => attempt.AttemptedAt).ThenBy(attempt => attempt.Id),
            "userName" => attempts.OrderBy(attempt => attempt.UserName).ThenByDescending(attempt => attempt.AttemptedAt),
            "-userName" => attempts.OrderByDescending(attempt => attempt.UserName).ThenByDescending(attempt => attempt.AttemptedAt),
            _ => attempts.OrderByDescending(attempt => attempt.AttemptedAt).ThenByDescending(attempt => attempt.Id),
        };

        var items = await sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return (items, total);
    }
}
