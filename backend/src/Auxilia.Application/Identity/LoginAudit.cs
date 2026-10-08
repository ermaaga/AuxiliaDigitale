using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

internal sealed class PasswordAuthenticationMethod : IAuthenticationMethod
{
    public string Code => LoginMethods.Password;

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class EmailOtpAuthenticationMethod(ISettingsProvider settings) : IAuthenticationMethod
{
    public string Code => LoginMethods.EmailOtp;

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => settings.GetAsync(IdentitySettings.OtpLoginEnabled, cancellationToken);
}

/// <summary>Sign-in methods and the login audit (F35).</summary>
public interface ILoginAuditQueryService
{
    Task<LoginMethodsResponse> GetMethodsAsync(CancellationToken cancellationToken);

    /// <summary>Paged, newest first by default; <c>pageSize</c> at most 100 (skill auxilia-api-contract).</summary>
    Task<Result<PagedResponse<LoginAttemptResponse>>> ListAttemptsAsync(LoginAttemptQuery query, CancellationToken cancellationToken);
}

internal sealed class LoginAuditQueryService(
    IEnumerable<IAuthenticationMethod> methods, ILoginAttemptReader attempts, ISettingsProvider settings) : ILoginAuditQueryService
{
    public const int MaxPageSize = 100;

    private static readonly string[] Sorts = ["attemptedAt", "-attemptedAt", "userName", "-userName"];

    public async Task<LoginMethodsResponse> GetMethodsAsync(CancellationToken cancellationToken)
    {
        var enabled = new List<string>();
        foreach (var method in methods)
        {
            if (await method.IsEnabledAsync(cancellationToken))
            {
                enabled.Add(method.Code);
            }
        }

        return new LoginMethodsResponse(enabled, await settings.GetAsync(IdentitySettings.RememberMeDays, cancellationToken));
    }

    public async Task<Result<PagedResponse<LoginAttemptResponse>>> ListAttemptsAsync(LoginAttemptQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        if (query.Sort is { } sort && !Sorts.Contains(sort, StringComparer.Ordinal))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            errors["from"] = ["validation.paging.range"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        var (items, total) = await attempts.ListAsync(query, cancellationToken);
        return new PagedResponse<LoginAttemptResponse>(
            items.Select(item => new LoginAttemptResponse(
                item.Id, item.UserName, item.UserId, item.Method, item.AttemptedAt, item.Succeeded, item.FailureReason, item.IpAddress, item.UserAgent)).ToArray(),
            query.Page,
            query.PageSize,
            total);
    }
}
