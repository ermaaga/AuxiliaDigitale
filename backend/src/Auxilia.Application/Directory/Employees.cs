using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Identity.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Directory;

/// <summary>
/// Employees of the current tenant (F06): a person, a user account with the Employee role (Identity) and an optional
/// employee profile (default flag Q31, administrator Q32). An employee is identified by the user id, the id clients,
/// specializations and assignments refer to.
/// </summary>
public interface IEmployeeManager
{
    /// <summary>Creates person, account (e-mail = user name) and profile; with sign-in the activation e-mail leaves after the commit (D-06).</summary>
    Task<Result<CreateEmployeeResponse>> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Soft delete: hidden from every list, sign-in disabled; the clients in charge go to the default employee (or to
    /// nobody when there is none), history kept. The default employee cannot be deleted (Q31).
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Enables or disables sign-in (legacy Active/Inactive); the default employee cannot be disabled.</summary>
    Task<Result> SetSignInAsync(Guid id, bool canSignIn, CancellationToken cancellationToken);

    /// <summary>Q31: the employee becomes the only default employee (must be able to sign in).</summary>
    Task<Result> MakeDefaultAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Replaces the employee's set of Employee specializations.</summary>
    Task<Result> SetSpecializationsAsync(Guid id, IReadOnlyList<Guid>? specializationIds, CancellationToken cancellationToken);

    /// <summary>Q32: the administrator the employee reports to; <c>null</c> removes it.</summary>
    Task<Result> SetAdministratorAsync(Guid id, Guid? administratorUserId, CancellationToken cancellationToken);

    /// <summary>E-mails a new activation link (the account has no password yet).</summary>
    Task<Result<EmployeeInvitationResponse>> SendInvitationAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<EmployeePasswordResetResponse>> ResetPasswordAsync(Guid id, bool sendLink, CancellationToken cancellationToken);
}

public interface IEmployeeQueryService
{
    /// <summary>Server-side filters, sort and paging.</summary>
    Task<Result<PagedResponse<EmployeeListItemResponse>>> ListAsync(EmployeeListQuery query, CancellationToken cancellationToken);

    Task<Result<EmployeeDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Administrators an employee can report to (Administrator role, can sign in), by name (Q32).</summary>
    Task<IReadOnlyList<EmployeeAdministratorResponse>> AdministratorsAsync(CancellationToken cancellationToken);

    /// <summary>The active specializations of the Employee role, by name: what an employee can be given (F06).</summary>
    Task<IReadOnlyList<EmployeeSpecializationResponse>> SpecializationsAsync(CancellationToken cancellationToken);
}

/// <summary>The fields every employee needs (Q55: first and last name, birth date, e-mail), on top of the person rules.</summary>
internal static class EmployeeRules
{
    public static PersonDetails Details(string? firstName, string? lastName, string? email, DateOnly? birthDate, string? phone, string? fiscalCode) =>
        new(firstName, lastName, email, birthDate, phone, fiscalCode);

    /// <summary>The errors of the person rules plus the mandatory employee fields, one entry per field.</summary>
    public static Result Check(Result personResult, PersonDetails details)
    {
        var errors = new Dictionary<string, string[]>(personResult.IsFailure ? personResult.Error!.ValidationErrors : new Dictionary<string, string[]>(), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(details.Email))
        {
            errors.TryAdd("email", ["validation.person.email"]);
        }

        if (details.BirthDate is null)
        {
            errors.TryAdd("birthDate", ["validation.person.birthDate"]);
        }

        return errors.Count > 0 ? Errors.Directory.PersonInvalid(errors) : Result.Success();
    }
}

internal sealed class EmployeeManager(
    IOperationRunner operations,
    IEmployeeDataFactory data,
    IUserAccounts accounts,
    TimeProvider clock,
    ILogger<EmployeeManager> logger) : IEmployeeManager
{
    public const int MaxSpecializations = 50;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<Result<CreateEmployeeResponse>> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await operations.RunAsync(Operations.Directory.CreateEmployee, null, async scope =>
        {
            var details = EmployeeRules.Details(request.FirstName, request.LastName, request.Email, request.BirthDate, request.Phone, request.FiscalCode);
            var person = Person.Create(Guid.CreateVersion7(), details, Today);
            var checkedPerson = EmployeeRules.Check(person.IsFailure ? Result.Failure(person.Error!) : Result.Success(), details);
            if (checkedPerson.IsFailure)
            {
                return Result.Failure<Guid>(checkedPerson.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (person.Value.FiscalCode is { } fiscalCode && await store.FiscalCodeTakenAsync(fiscalCode, null, cancellationToken))
            {
                return Errors.Directory.FiscalCodeTaken();
            }

            store.Add(person.Value);
            await store.SaveChangesAsync(cancellationToken);

            // F06: the e-mail is the user name.
            var user = await accounts.CreateAsync(
                person.Value.Id, person.Value.Email!, person.Value.Email, TenantRole.Employee, request.CanSignIn, cancellationToken);
            if (user.IsFailure)
            {
                return user;
            }

            store.Add(EmployeeProfile.Create(user.Value));
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Employee", user.Value);
            return user;
        }, cancellationToken);

        if (created.IsFailure)
        {
            return Result.Failure<CreateEmployeeResponse>(created.Error!);
        }

        // The employee exists even when the e-mail cannot leave: the invitation can be sent again later.
        if (!request.CanSignIn)
        {
            return new CreateEmployeeResponse(created.Value, false, null);
        }

        var invitation = await InviteAsync(created.Value, cancellationToken);
        return new CreateEmployeeResponse(created.Value, invitation.Sent, invitation.ErrorCode);
    }

    public Task<Result> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Directory.UpdateEmployee, new { EmployeeUserId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindPersonAsync(id, cancellationToken) is not { } person)
            {
                return Errors.Directory.EmployeeNotFound();
            }

            var details = EmployeeRules.Details(request.FirstName, request.LastName, request.Email, request.BirthDate, request.Phone, request.FiscalCode);
            var checkedPerson = EmployeeRules.Check(person.Update(details, Today), details);
            if (checkedPerson.IsFailure)
            {
                return checkedPerson;
            }

            // Q54: the fiscal code stays unique on edit too.
            if (person.FiscalCode is { } fiscalCode && await store.FiscalCodeTakenAsync(fiscalCode, person.Id, cancellationToken))
            {
                return Errors.Directory.FiscalCodeTaken();
            }

            await store.SaveChangesAsync(cancellationToken);

            // Q52: the user name may differ from the e-mail but stays unique.
            return await accounts.UpdateAsync(id, request.UserName, person.Email, cancellationToken);
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.DeleteEmployee, new { EmployeeUserId = id }, async _ =>
        {
            // Two units of work in the operation's transaction: the clients are handed over first, then the person is
            // removed without anything else tracked (as for clients, the soft delete never cascades).
            await using (var store = await data.OpenAsync(cancellationToken))
            {
                if (await store.FindPersonAsync(id, cancellationToken) is null)
                {
                    return Errors.Directory.EmployeeNotFound();
                }

                var fallback = await store.FindDefaultAsync(cancellationToken);
                if (fallback?.Id == id)
                {
                    return Errors.Directory.EmployeeIsDefault();
                }

                var now = clock.GetUtcNow();
                var successor = fallback is not null && await IsActiveEmployeeAsync(fallback.Id, cancellationToken) ? fallback.Id : (Guid?)null;
                foreach (var client in await store.ClientsInChargeAsync(id, cancellationToken))
                {
                    if (successor is { } employee)
                    {
                        client.Assign(employee, now);
                    }
                    else
                    {
                        client.Unassign(now);
                    }
                }

                await store.SaveChangesAsync(cancellationToken);
            }

            await using (var people = await data.OpenAsync(cancellationToken))
            {
                if (await people.FindPersonAsync(id, cancellationToken) is not { } person)
                {
                    return Errors.Directory.EmployeeNotFound();
                }

                people.Remove(person);
                await people.SaveChangesAsync(cancellationToken);
            }

            return await accounts.SetSignInAsync(id, false, cancellationToken);
        }, cancellationToken);

    public Task<Result> SetSignInAsync(Guid id, bool canSignIn, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeEmployeeSignIn, new { EmployeeUserId = id, CanSignIn = canSignIn }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindPersonAsync(id, cancellationToken) is null)
            {
                return Errors.Directory.EmployeeNotFound();
            }

            if (!canSignIn && (await store.FindProfileAsync(id, cancellationToken))?.IsDefault == true)
            {
                return Errors.Directory.EmployeeIsDefault();
            }

            return await accounts.SetSignInAsync(id, canSignIn, cancellationToken);
        }, cancellationToken);

    public Task<Result> MakeDefaultAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeDefaultEmployee, new { EmployeeUserId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindPersonAsync(id, cancellationToken) is null)
            {
                return Errors.Directory.EmployeeNotFound();
            }

            if (!await IsActiveEmployeeAsync(id, cancellationToken))
            {
                return Errors.Directory.DefaultEmployeeInactive();
            }

            // Q31: exactly one default. The previous one is cleared and saved first, so the unique index never sees two.
            if (await store.FindDefaultAsync(cancellationToken) is { } previous)
            {
                if (previous.Id == id)
                {
                    return Result.Success();
                }

                previous.ClearDefault();
                await store.SaveChangesAsync(cancellationToken);
            }

            (await ProfileAsync(store, id, cancellationToken)).MakeDefault();
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SetSpecializationsAsync(Guid id, IReadOnlyList<Guid>? specializationIds, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeEmployeeSpecializations, new { EmployeeUserId = id }, async _ =>
        {
            var wanted = (specializationIds ?? []).Distinct().ToHashSet();
            if (wanted.Count > MaxSpecializations)
            {
                return Errors.Directory.EmployeeSpecializationInvalid();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindPersonAsync(id, cancellationToken) is null)
            {
                return Errors.Directory.EmployeeNotFound();
            }

            var specializations = await store.EmployeeSpecializationsAsync(cancellationToken);
            if (!wanted.IsSubsetOf(specializations.Select(specialization => specialization.Id)))
            {
                return Errors.Directory.EmployeeSpecializationInvalid();
            }

            var now = clock.GetUtcNow();
            var changed = false;
            foreach (var specialization in specializations)
            {
                changed |= wanted.Contains(specialization.Id)
                    ? specialization.AddMembers([id], now) > 0
                    : specialization.RemoveMember(id);
            }

            if (changed)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result> SetAdministratorAsync(Guid id, Guid? administratorUserId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeEmployeeAdministrator, new { EmployeeUserId = id, AdministratorUserId = administratorUserId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindPersonAsync(id, cancellationToken) is null)
            {
                return Errors.Directory.EmployeeNotFound();
            }

            if (administratorUserId is { } administrator
                && !(await accounts.FindManyAsync([administrator], cancellationToken))
                    .Any(account => account.CanSignIn && account.Roles.Contains(TenantRole.Administrator)))
            {
                return Errors.Directory.AdministratorInvalid();
            }

            var profile = await store.FindProfileAsync(id, cancellationToken);
            if (profile is null && administratorUserId is null)
            {
                return Result.Success();
            }

            if ((profile ?? await ProfileAsync(store, id, cancellationToken)).SetAdministrator(administratorUserId))
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);

    public async Task<Result<EmployeeInvitationResponse>> SendInvitationAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await AccountOfEmployeeAsync(id, cancellationToken) is not { } account)
        {
            return Errors.Directory.EmployeeNotFound();
        }

        if (account.IsActivated)
        {
            return Errors.Identity.AccountAlreadyActivated();
        }

        return await InviteAsync(account.UserId, cancellationToken);
    }

    public async Task<Result<EmployeePasswordResetResponse>> ResetPasswordAsync(Guid id, bool sendLink, CancellationToken cancellationToken)
    {
        if (await AccountOfEmployeeAsync(id, cancellationToken) is not { } account)
        {
            return Errors.Directory.EmployeeNotFound();
        }

        var reset = await accounts.ResetPasswordAsync(account.UserId, sendLink, cancellationToken);
        return reset.IsFailure
            ? Result.Failure<EmployeePasswordResetResponse>(reset.Error!)
            : new EmployeePasswordResetResponse(reset.Value.TemporaryPassword);
    }

    /// <summary>The profile of the employee, added to the unit of work when the employee has none yet.</summary>
    private static async Task<EmployeeProfile> ProfileAsync(IEmployeeData store, Guid id, CancellationToken cancellationToken)
    {
        if (await store.FindProfileAsync(id, cancellationToken) is { } profile)
        {
            return profile;
        }

        var created = EmployeeProfile.Create(id);
        store.Add(created);
        return created;
    }

    private async Task<UserAccount?> AccountOfEmployeeAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindPersonAsync(id, cancellationToken) is null
            ? null
            : (await accounts.FindManyAsync([id], cancellationToken)).SingleOrDefault();
    }

    private async Task<bool> IsActiveEmployeeAsync(Guid userId, CancellationToken cancellationToken) =>
        (await accounts.FindManyAsync([userId], cancellationToken))
            .Any(account => account.CanSignIn && account.Roles.Contains(TenantRole.Employee));

    private async Task<EmployeeInvitationResponse> InviteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sent = await accounts.SendActivationAsync(userId, cancellationToken);
        if (sent.IsFailure)
        {
            Log.Identity.InvitationPending(logger, userId, sent.Error!.DisplayCode);
            return new EmployeeInvitationResponse(false, sent.Error.DisplayCode);
        }

        return new EmployeeInvitationResponse(true, null);
    }
}

internal sealed class EmployeeQueryService(IEmployeeDataFactory data, IUserAccounts accounts) : IEmployeeQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxFilterLength = 200;
    public const string StatusActive = "active";
    public const string StatusInactive = "inactive";

    private static readonly Dictionary<string, EmployeeSort> Sorts = new(StringComparer.Ordinal)
    {
        ["lastName"] = EmployeeSort.LastName,
        ["fullName"] = EmployeeSort.FullName,
        ["email"] = EmployeeSort.Email,
        ["userName"] = EmployeeSort.UserName,
    };

    public async Task<Result<PagedResponse<EmployeeListItemResponse>>> ListAsync(EmployeeListQuery query, CancellationToken cancellationToken)
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

        var sortField = query.Sort?.TrimStart('-');
        var sort = EmployeeSort.LastName;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        bool? canSignIn = query.Status switch
        {
            null or "" => null,
            StatusActive => true,
            StatusInactive => false,
            _ => null,
        };
        if (!string.IsNullOrEmpty(query.Status) && canSignIn is null)
        {
            errors["status"] = ["validation.employees.status"];
        }

        string?[] texts = [query.FullName, query.LastName, query.Email, query.UserName, query.Phone];
        if (texts.Any(text => text is { Length: > MaxFilterLength }))
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new EmployeeFilter(
                Text(query.FullName), Text(query.LastName), Text(query.Email), Text(query.UserName), Text(query.Phone), canSignIn,
                sort, query.Sort?.StartsWith('-') == true, (query.Page - 1) * query.PageSize, query.PageSize),
            cancellationToken);
        var specializations = (await store.SpecializationsOfAsync(items.Select(item => item.UserId).ToArray(), cancellationToken))
            .ToLookup(row => row.UserId, row => new EmployeeSpecializationResponse(row.SpecializationId, row.Name));

        return new PagedResponse<EmployeeListItemResponse>(
            items.Select(row => new EmployeeListItemResponse(
                row.UserId,
                row.FirstName,
                row.LastName,
                row.Email,
                row.UserName,
                row.Phone,
                row.CanSignIn,
                row.IsDefault,
                row.AssignedClients,
                specializations[row.UserId].ToArray(),
                row.ImageVersion)).ToArray(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<Result<EmployeeDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindPersonAsync(id, cancellationToken) is not { } person
            || (await accounts.FindManyAsync([id], cancellationToken)).SingleOrDefault() is not { } account)
        {
            return Errors.Directory.EmployeeNotFound();
        }

        var profile = await store.FindProfileAsync(id, cancellationToken);
        var administrator = profile?.AdministratorUserId is { } administratorId
            ? (await store.NamesAsync([administratorId], cancellationToken))
                .Select(name => new EmployeeAdministratorResponse(name.UserId, name.FullName))
                .SingleOrDefault()
            : null;
        var specializations = (await store.SpecializationsOfAsync([id], cancellationToken))
            .Select(row => new EmployeeSpecializationResponse(row.SpecializationId, row.Name))
            .ToArray();

        return new EmployeeDetailResponse(
            account.UserId,
            person.Id,
            person.FirstName,
            person.LastName,
            person.Email,
            person.BirthDate,
            person.Phone,
            person.FiscalCode,
            account.UserName,
            account.CanSignIn,
            account.IsActivated,
            profile?.IsDefault == true,
            await store.CreatedAtAsync(id, cancellationToken) ?? default,
            administrator,
            specializations,
            new EmployeeWorkloadResponse(await store.CountClientsInChargeAsync(id, cancellationToken)),
            await store.ImageVersionAsync(id, cancellationToken));
    }

    public async Task<IReadOnlyList<EmployeeAdministratorResponse>> AdministratorsAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.AdministratorsAsync(cancellationToken))
            .Select(administrator => new EmployeeAdministratorResponse(administrator.UserId, administrator.FullName))
            .ToArray();
    }

    public async Task<IReadOnlyList<EmployeeSpecializationResponse>> SpecializationsAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.EmployeeSpecializationsAsync(cancellationToken))
            .Select(specialization => new EmployeeSpecializationResponse(specialization.Id, specialization.Name))
            .ToArray();
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
