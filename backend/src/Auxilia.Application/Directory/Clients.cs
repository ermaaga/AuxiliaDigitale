using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Configuration.Public;
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
/// Clients of the current tenant (F05, D-05, D-06): a person, a client profile and a sign-in account (Identity). Staff
/// create, edit and delete (soft, Q29) them, assign the employee in charge (history kept), enable sign-in (only with
/// an employee, Q60), set their specializations (Q30) and help them in (activation link, password reset).
/// </summary>
public interface IClientManager
{
    /// <summary>
    /// Administrator: the client can sign in and is assigned to the employee given, otherwise to the default employee
    /// (Q31) if any. Employee: the client cannot sign in yet and is assigned to that employee. The activation e-mail
    /// leaves after the commit (D-06).
    /// </summary>
    Task<Result<CreateClientResponse>> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete: hidden from every list, sign-in disabled, nobody in charge any more.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> SetSignInAsync(Guid id, bool canSignIn, CancellationToken cancellationToken);

    Task<Result> AssignAsync(Guid id, Guid employeeUserId, CancellationToken cancellationToken);

    Task<Result> UnassignAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Replaces the client's set of Client specializations.</summary>
    Task<Result> SetSpecializationsAsync(Guid id, IReadOnlyList<Guid>? specializationIds, CancellationToken cancellationToken);

    /// <summary>E-mails a new activation link (the account has no password yet).</summary>
    Task<Result<ClientInvitationResponse>> SendInvitationAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<ClientPasswordResetResponse>> ResetPasswordAsync(Guid id, bool sendLink, CancellationToken cancellationToken);
}

public interface IClientQueryService
{
    /// <summary>Server-side filters, sort and paging; view <c>mine</c> = the clients of the calling employee.</summary>
    Task<Result<PagedResponse<ClientListItemResponse>>> ListAsync(ClientListQuery query, CancellationToken cancellationToken);

    Task<Result<ClientDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Employees a client can be assigned to (Employee role, can sign in), by name.</summary>
    Task<IReadOnlyList<ClientEmployeeResponse>> AssignableEmployeesAsync(CancellationToken cancellationToken);

    /// <summary>The active specializations of the Client role, by name: what a client can be given (Q30).</summary>
    Task<IReadOnlyList<ClientSpecializationResponse>> SpecializationsAsync(CancellationToken cancellationToken);
}

/// <summary>Validation shared by create and update: the fields every client needs (legacy staff form), on top of the person rules.</summary>
internal static class ClientRules
{
    public const string CustomFieldEntity = "client";

    public static PersonDetails Details(string? firstName, string? lastName, string? email, DateOnly? birthDate, string? phone, string? fiscalCode) =>
        new(firstName, lastName, email, birthDate, phone, fiscalCode);

    /// <summary>The errors of the person rules plus the mandatory client fields, one entry per field.</summary>
    public static Result Check(Result personResult, PersonDetails details)
    {
        var errors = new Dictionary<string, string[]>(personResult.IsFailure ? personResult.Error!.ValidationErrors : new Dictionary<string, string[]>(), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(details.Email))
        {
            errors.TryAdd("email", ["validation.person.email"]);
        }

        if (string.IsNullOrWhiteSpace(details.FiscalCode))
        {
            errors.TryAdd("fiscalCode", ["validation.person.fiscalCode"]);
        }

        if (details.BirthDate is null)
        {
            errors.TryAdd("birthDate", ["validation.person.birthDate"]);
        }

        return errors.Count > 0 ? Errors.Directory.PersonInvalid(errors) : Result.Success();
    }
}

internal sealed class ClientManager(
    IOperationRunner operations,
    IClientDataFactory data,
    IUserAccounts accounts,
    ICustomFieldValidator customFields,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<ClientManager> logger) : IClientManager
{
    public const int MaxSpecializations = 50;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>An employee (not also an Administrator) creates clients for themselves (legacy <c>CreateClientForm</c>).</summary>
    private bool CallerIsEmployeeOnly =>
        currentUser.Roles.Contains(TenantRole.Employee) && !currentUser.Roles.Contains(TenantRole.Administrator);

    public async Task<Result<CreateClientResponse>> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await operations.RunAsync(Operations.Directory.CreateClient, null, async scope =>
        {
            var details = ClientRules.Details(request.FirstName, request.LastName, request.Email, request.BirthDate, request.Phone, request.FiscalCode);
            var person = Person.Create(Guid.CreateVersion7(), details, Today);
            var checkedPerson = ClientRules.Check(person.IsFailure ? Result.Failure(person.Error!) : Result.Success(), details);
            if (checkedPerson.IsFailure)
            {
                return Result.Failure<(Guid, Guid)>(checkedPerson.Error!);
            }

            var fields = await customFields.ValidateAsync(ClientRules.CustomFieldEntity, request.CustomFields, cancellationToken);
            if (fields.IsFailure)
            {
                return Result.Failure<(Guid, Guid)>(fields.Error!);
            }

            var employeeOnly = CallerIsEmployeeOnly;
            var employee = employeeOnly ? currentUser.UserId : request.EmployeeUserId;
            if (!employeeOnly && employee is { } employeeId && !await IsAssignableAsync(employeeId, cancellationToken))
            {
                return Errors.Directory.EmployeeInvalid();
            }

            await using var store = await data.OpenAsync(cancellationToken);

            // Q31: without an employee the client goes to the default employee, when there is one who can sign in.
            employee ??= await store.DefaultEmployeeAsync(cancellationToken);
            if (await store.FiscalCodeTakenAsync(person.Value.FiscalCode!, null, cancellationToken))
            {
                return Errors.Directory.FiscalCodeTaken();
            }

            person.Value.SetCustomFields(fields.Value);
            store.Add(person.Value);
            store.Add(ClientProfile.Create(person.Value.Id, employee, clock.GetUtcNow()));
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Client", person.Value.Id);

            // F05: the e-mail is the user name. Created by an employee: no sign-in until staff enables it (Q60).
            var user = await accounts.CreateAsync(person.Value.Id, person.Value.Email!, person.Value.Email, TenantRole.Client, !employeeOnly, cancellationToken);
            return user.IsFailure ? Result.Failure<(Guid, Guid)>(user.Error!) : Result.Success((person.Value.Id, user.Value));
        }, cancellationToken);

        if (created.IsFailure)
        {
            return Result.Failure<CreateClientResponse>(created.Error!);
        }

        // The client exists even when the e-mail cannot leave: the invitation can be sent again later.
        var (clientId, userId) = created.Value;
        if (CallerIsEmployeeOnly)
        {
            return new CreateClientResponse(clientId, false, null);
        }

        var invitation = await InviteAsync(userId, cancellationToken);
        return new CreateClientResponse(clientId, invitation.Sent, invitation.ErrorCode);
    }

    public Task<Result> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Directory.UpdateClient, new { ClientId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindPersonAsync(id, cancellationToken) is not { } person)
            {
                return Errors.Directory.ClientNotFound();
            }

            var details = ClientRules.Details(request.FirstName, request.LastName, request.Email, request.BirthDate, request.Phone, request.FiscalCode);
            var fiscalCode = Person.NormalizeFiscalCode(request.FiscalCode);
            var checkedPerson = ClientRules.Check(person.Update(details, Today), details);
            if (checkedPerson.IsFailure)
            {
                return checkedPerson;
            }

            var fields = await customFields.ValidateAsync(ClientRules.CustomFieldEntity, request.CustomFields, cancellationToken);
            if (fields.IsFailure)
            {
                return Result.Failure(fields.Error!);
            }

            // Q54: the fiscal code stays unique on edit too.
            if (await store.FiscalCodeTakenAsync(fiscalCode!, id, cancellationToken))
            {
                return Errors.Directory.FiscalCodeTaken();
            }

            person.SetCustomFields(fields.Value);
            await store.SaveChangesAsync(cancellationToken);

            if (await accounts.FindByPersonAsync(id, cancellationToken) is not { } account)
            {
                return Result.Success();
            }

            // Q52: the user name may differ from the e-mail but stays unique.
            return await accounts.UpdateAsync(account.UserId, request.UserName, person.Email, cancellationToken);
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.DeleteClient, new { ClientId = id }, async _ =>
        {
            // Two units of work in the operation's transaction: the person is removed without its profile tracked,
            // so the soft delete never cascades to the profile and its assignment history.
            await using (var profiles = await data.OpenAsync(cancellationToken))
            {
                if (await profiles.FindProfileAsync(id, cancellationToken) is not { } profile)
                {
                    return Errors.Directory.ClientNotFound();
                }

                if (profile.Unassign(clock.GetUtcNow()))
                {
                    await profiles.SaveChangesAsync(cancellationToken);
                }
            }

            await using (var people = await data.OpenAsync(cancellationToken))
            {
                if (await people.FindPersonAsync(id, cancellationToken) is not { } person)
                {
                    return Errors.Directory.ClientNotFound();
                }

                people.Remove(person);
                await people.SaveChangesAsync(cancellationToken);
            }

            return await accounts.FindByPersonAsync(id, cancellationToken) is { CanSignIn: true } account
                ? await accounts.SetSignInAsync(account.UserId, false, cancellationToken)
                : Result.Success();
        }, cancellationToken);

    public Task<Result> SetSignInAsync(Guid id, bool canSignIn, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeClientSignIn, new { ClientId = id, CanSignIn = canSignIn }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindProfileAsync(id, cancellationToken) is not { } profile)
            {
                return Errors.Directory.ClientNotFound();
            }

            if (canSignIn && profile.CanEnableSignIn() is { IsFailure: true } refused)
            {
                return refused;
            }

            return await accounts.FindByPersonAsync(id, cancellationToken) is { } account
                ? await accounts.SetSignInAsync(account.UserId, canSignIn, cancellationToken)
                : Errors.Directory.ClientNotFound();
        }, cancellationToken);

    public Task<Result> AssignAsync(Guid id, Guid employeeUserId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeClientAssignment, new { ClientId = id, EmployeeUserId = employeeUserId }, async _ =>
        {
            if (!await IsAssignableAsync(employeeUserId, cancellationToken))
            {
                return Errors.Directory.EmployeeInvalid();
            }

            return await ChangeProfileAsync(id, profile => profile.Assign(employeeUserId, clock.GetUtcNow()), cancellationToken);
        }, cancellationToken);

    public Task<Result> UnassignAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeClientAssignment, new { ClientId = id }, _ =>
            ChangeProfileAsync(id, profile => profile.Unassign(clock.GetUtcNow()), cancellationToken), cancellationToken);

    public Task<Result> SetSpecializationsAsync(Guid id, IReadOnlyList<Guid>? specializationIds, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.ChangeClientSpecializations, new { ClientId = id }, async _ =>
        {
            var wanted = (specializationIds ?? []).Distinct().ToHashSet();
            if (wanted.Count > MaxSpecializations)
            {
                return Errors.Directory.ClientSpecializationInvalid();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindProfileAsync(id, cancellationToken) is null
                || await accounts.FindByPersonAsync(id, cancellationToken) is not { } account)
            {
                return Errors.Directory.ClientNotFound();
            }

            var specializations = await store.ClientSpecializationsAsync(cancellationToken);
            if (!wanted.IsSubsetOf(specializations.Select(specialization => specialization.Id)))
            {
                return Errors.Directory.ClientSpecializationInvalid();
            }

            var now = clock.GetUtcNow();
            var changed = false;
            foreach (var specialization in specializations)
            {
                changed |= wanted.Contains(specialization.Id)
                    ? specialization.AddMembers([account.UserId], now) > 0
                    : specialization.RemoveMember(account.UserId);
            }

            if (changed)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);

    public async Task<Result<ClientInvitationResponse>> SendInvitationAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await AccountOfClientAsync(id, cancellationToken) is not { } account)
        {
            return Errors.Directory.ClientNotFound();
        }

        if (account.IsActivated)
        {
            return Errors.Identity.AccountAlreadyActivated();
        }

        return await InviteAsync(account.UserId, cancellationToken);
    }

    public async Task<Result<ClientPasswordResetResponse>> ResetPasswordAsync(Guid id, bool sendLink, CancellationToken cancellationToken)
    {
        if (await AccountOfClientAsync(id, cancellationToken) is not { } account)
        {
            return Errors.Directory.ClientNotFound();
        }

        var reset = await accounts.ResetPasswordAsync(account.UserId, sendLink, cancellationToken);
        return reset.IsFailure
            ? Result.Failure<ClientPasswordResetResponse>(reset.Error!)
            : new ClientPasswordResetResponse(reset.Value.TemporaryPassword);
    }

    private async Task<UserAccount?> AccountOfClientAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindProfileAsync(id, cancellationToken) is null ? null : await accounts.FindByPersonAsync(id, cancellationToken);
    }

    private async Task<ClientInvitationResponse> InviteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sent = await accounts.SendActivationAsync(userId, cancellationToken);
        if (sent.IsFailure)
        {
            Log.Identity.InvitationPending(logger, userId, sent.Error!.DisplayCode);
            return new ClientInvitationResponse(false, sent.Error.DisplayCode);
        }

        return new ClientInvitationResponse(true, null);
    }

    private async Task<bool> IsAssignableAsync(Guid employeeUserId, CancellationToken cancellationToken) =>
        (await accounts.FindManyAsync([employeeUserId], cancellationToken))
            .Any(account => account.CanSignIn && account.Roles.Contains(TenantRole.Employee));

    private async Task<Result> ChangeProfileAsync(Guid id, Func<ClientProfile, bool> change, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindProfileAsync(id, cancellationToken) is not { } profile)
        {
            return Errors.Directory.ClientNotFound();
        }

        if (change(profile))
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class ClientQueryService(IClientDataFactory data, IUserAccounts accounts, ICurrentUser currentUser) : IClientQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxFilterLength = 200;
    public const string ViewMine = "mine";
    public const string ViewAll = "all";

    private static readonly Dictionary<string, ClientSort> Sorts = new(StringComparer.Ordinal)
    {
        ["lastName"] = ClientSort.LastName,
        ["fullName"] = ClientSort.FullName,
        ["email"] = ClientSort.Email,
        ["userName"] = ClientSort.UserName,
    };

    public async Task<Result<PagedResponse<ClientListItemResponse>>> ListAsync(ClientListQuery query, CancellationToken cancellationToken)
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

        var view = string.IsNullOrEmpty(query.View) ? ViewAll : query.View;
        if (view is not (ViewMine or ViewAll))
        {
            errors["view"] = ["validation.clients.view"];
        }

        var sortField = query.Sort?.TrimStart('-');
        var sort = ClientSort.LastName;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        ClientStatus? status = null;
        if (!string.IsNullOrEmpty(query.Status))
        {
            if (Enum.TryParse<ClientStatus>(query.Status, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
            {
                status = parsed;
            }
            else
            {
                errors["status"] = ["validation.clients.status"];
            }
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

        // "My clients" of a caller who is not an employee is empty, never everyone.
        var mine = view == ViewMine ? currentUser.UserId ?? Guid.Empty : query.EmployeeUserId;
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new ClientFilter(
                mine, Text(query.FullName), Text(query.LastName), Text(query.Email), Text(query.UserName), Text(query.Phone), status,
                sort, query.Sort?.StartsWith('-') == true, (query.Page - 1) * query.PageSize, query.PageSize, query.TagId),
            cancellationToken);

        return new PagedResponse<ClientListItemResponse>(
            items.Select(row => new ClientListItemResponse(
                row.Id,
                row.FirstName,
                row.LastName,
                row.Email,
                row.UserName,
                row.Phone,
                row.FiscalCode,
                row.Status.ToString(),
                row.CanSignIn,
                row.EmployeeUserId is { } employeeId ? new ClientEmployeeResponse(employeeId, row.EmployeeName ?? string.Empty) : null,
                Json(row.CustomFields),
                row.UserId,
                row.ImageVersion)).ToArray(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<Result<ClientDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindPersonAsync(id, cancellationToken) is not { } person
            || await store.FindProfileAsync(id, cancellationToken) is not { } profile)
        {
            return Errors.Directory.ClientNotFound();
        }

        var account = await accounts.FindByPersonAsync(id, cancellationToken);
        var assignments = profile.Assignments;
        var names = (await store.NamesAsync(assignments.Select(item => item.EmployeeUserId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(item => item.UserId, item => item.FullName);
        var specializations = account is null
            ? []
            : (await store.ClientSpecializationsAsync(cancellationToken))
                .Where(specialization => specialization.Members.Any(member => member.UserId == account.UserId))
                .Select(specialization => new ClientSpecializationResponse(specialization.Id, specialization.Name))
                .ToArray();

        return new ClientDetailResponse(
            person.Id,
            person.FirstName,
            person.LastName,
            person.Email,
            person.BirthDate,
            person.Phone,
            person.FiscalCode,
            profile.Status.ToString(),
            profile.StatusChangedAt,
            Json(person.CustomFields),
            account is null ? null : new ClientAccountResponse(account.UserId, account.UserName, account.CanSignIn, account.IsActivated),
            profile.EmployeeUserId is { } employeeId ? new ClientEmployeeResponse(employeeId, names.GetValueOrDefault(employeeId, string.Empty)) : null,
            assignments
                .OrderByDescending(item => item.AssignedAt)
                .Select(item => new ClientAssignmentResponse(item.EmployeeUserId, names.GetValueOrDefault(item.EmployeeUserId), item.AssignedAt, item.EndedAt))
                .ToArray(),
            specializations,
            account is null ? null : await store.ImageVersionAsync(account.UserId, cancellationToken));
    }

    public async Task<IReadOnlyList<ClientEmployeeResponse>> AssignableEmployeesAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.AssignableEmployeesAsync(cancellationToken))
            .Select(employee => new ClientEmployeeResponse(employee.UserId, employee.FullName))
            .ToArray();
    }

    public async Task<IReadOnlyList<ClientSpecializationResponse>> SpecializationsAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.ClientSpecializationsAsync(cancellationToken))
            .Select(specialization => new ClientSpecializationResponse(specialization.Id, specialization.Name))
            .ToArray();
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
