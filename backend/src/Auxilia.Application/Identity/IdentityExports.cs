using Auxilia.Application.Abstractions.Exports;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>The login audit (F35, F26), as <c>GET /identity/login-attempts</c>.</summary>
internal sealed class LoginAttemptExportSource(ILoginAuditQueryService audit) : IExportSource
{
    public string Key => "login-attempts";

    public string TitleKey => "app.exports.loginAttempts";

    public string Permission => IdentityPermissions.ViewLoginAttempts;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("attemptedAt", "Date"),
        new("userName", "Username"),
        new("method", "LoginType", Translated: true),
        new("result", "LoginResult", Translated: true),
        new("failureReason", "FailureReason", Translated: true),
        new("ipAddress", "app.identity.loginAttempts.ipAddress"),
        new("userAgent", "app.identity.loginAttempts.userAgent"),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var query = new LoginAttemptQuery(
            parameters.Text("filter[userName]"), parameters.Text("filter[method]"), parameters.Bool("filter[succeeded]"), parameters.Instant("filter[from]"),
            parameters.Instant("filter[to]"), parameters.Text("sort"), page, pageSize);
        if (parameters.Errors is { } invalid)
        {
            return invalid;
        }

        var result = await audit.ListAttemptsAsync(query, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["attemptedAt"] = formatting.DateTime(item.AttemptedAt),
                    ["userName"] = item.UserName,
                    ["method"] = $"app.identity.loginMethods.{item.Method}",
                    ["result"] = item.Succeeded ? "app.identity.loginAttempts.succeeded" : "Failed",
                    ["failureReason"] = item.FailureReason is { } reason ? $"app.identity.loginFailures.{reason}" : string.Empty,
                    ["ipAddress"] = item.IpAddress ?? string.Empty,
                    ["userAgent"] = item.UserAgent ?? string.Empty,
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}

/// <summary>The open sessions (F17, F26), as <c>GET /identity/sessions</c>.</summary>
internal sealed class ActiveSessionExportSource(IActiveSessionQueryService sessions) : IExportSource
{
    public string Key => "sessions";

    public string TitleKey => "ActiveSessions";

    public string Permission => IdentityPermissions.ViewSessions;

    public IReadOnlyList<ExportColumn> Columns { get; } =
    [
        new("user", "app.sessions.user"),
        new("userName", "Username"),
        new("roles", "app.sessions.roles"),
        new("client", "app.sessions.client"),
        new("ip", "app.sessions.ip"),
        new("userAgent", "app.sessions.userAgent"),
        new("createdAt", "app.sessions.started"),
        new("lastUsedAt", "app.sessions.lastUsed"),
    ];

    public async Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(formatting);
        var result = await sessions.ListAsync(new ActiveSessionQuery(parameters.Text("filter[userName]"), parameters.Text("sort"), page, pageSize), null, cancellationToken);
        return result.IsFailure
            ? Result.Failure<ExportPage>(result.Error!)
            : new ExportPage(
                result.Value.Items.Select(item => new ExportRow(item.Id, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["user"] = item.FullName,
                    ["userName"] = item.UserName,
                    ["roles"] = string.Join(", ", item.Roles),
                    ["client"] = item.ClientId,
                    ["ip"] = item.IpAddress ?? string.Empty,
                    ["userAgent"] = item.UserAgent ?? string.Empty,
                    ["createdAt"] = formatting.DateTime(item.CreatedAt),
                    ["lastUsedAt"] = formatting.DateTime(item.LastUsedAt),
                })).ToArray(),
                (int)result.Value.TotalCount);
    }
}
