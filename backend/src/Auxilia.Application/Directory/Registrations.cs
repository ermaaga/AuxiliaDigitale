using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Captcha;
using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Localization.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Directory;

/// <summary>The client application calling a public registration endpoint (headers <c>X-Client-Id</c> / <c>X-Client-Secret</c>).</summary>
public sealed record RegistrationCaller(string? ClientId, string? ClientSecret);

/// <summary>
/// External registration (F02/F03, API only D-14): a registered client application sends requests (captcha of the
/// client application, IP rate limit at the endpoint); Administrators and Employees approve them (a client is created,
/// assigned to the default employee, Q31) or reject them, once.
/// </summary>
public interface IRegistrationManager
{
    /// <summary>
    /// The captcha the client application must solve before <see cref="SubmitAsync"/>; also tells whether the tenant
    /// accepts registrations (<c>AUX-13037</c> when not).
    /// </summary>
    Task<Result<CaptchaChallenge>> CreateCaptchaAsync(RegistrationCaller caller, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a pending request: one per e-mail among the pending ones, none for an e-mail already registered (Q05).
    /// The confirmation e-mail leaves when <c>registration.sendConfirmationEmail</c> is on; Administrators are notified
    /// in real time when <c>registration.notifyAdmins</c> is on.
    /// </summary>
    Task<Result<RegistrationSubmittedResponse>> SubmitAsync(SubmitRegistrationRequest request, RegistrationCaller caller, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the client (person, profile, account with user name = e-mail, D-06) and marks the request approved. With
    /// a default employee the client can sign in and gets the activation e-mail after the commit.
    /// </summary>
    Task<Result<ApproveRegistrationResponse>> ApproveAsync(Guid id, string? notes, CancellationToken cancellationToken);

    Task<Result> RejectAsync(Guid id, string? notes, CancellationToken cancellationToken);
}

public interface IRegistrationQueryService
{
    Task<Result<PagedResponse<RegistrationResponse>>> ListAsync(RegistrationListQuery query, CancellationToken cancellationToken);

    Task<Result<RegistrationResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class RegistrationManager(
    IOperationRunner operations,
    IRegistrationDataFactory data,
    IClientDataFactory clients,
    IUserAccounts accounts,
    IClientApplications clientApplications,
    IEnumerable<ICaptchaVerifier> captchas,
    ISettingsProvider settings,
    IModuleAccess modules,
    ITenantLanguages languages,
    IMessageDispatcher messages,
    IRealtimeNotifier notifier,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<RegistrationManager> logger) : IRegistrationManager
{
    /// <summary>Roles that review registrations: without one of them seeing the directory nobody could ever process a request.</summary>
    private static readonly TenantRole[] Reviewers = [TenantRole.Administrator, TenantRole.Employee];

    public async Task<Result<CaptchaChallenge>> CreateCaptchaAsync(RegistrationCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var open = await OpenAsync(caller, cancellationToken);
        if (open.IsFailure)
        {
            return Result.Failure<CaptchaChallenge>(open.Error!);
        }

        var (client, verifier) = open.Value;
        return new CaptchaChallenge(client.CaptchaProvider, await verifier.CreateChallengeAsync(cancellationToken));
    }

    public Task<Result<RegistrationSubmittedResponse>> SubmitAsync(SubmitRegistrationRequest request, RegistrationCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        return operations.RunAsync<RegistrationSubmittedResponse>(Operations.Directory.SubmitRegistration, new { ClientApplication = caller.ClientId }, async scope =>
        {
            var open = await OpenAsync(caller, cancellationToken);
            if (open.IsFailure)
            {
                return Result.Failure<RegistrationSubmittedResponse>(open.Error!);
            }

            var (client, verifier) = open.Value;
            var language = await languages.IsActiveAsync(request.Language, cancellationToken)
                ? request.Language!
                : await settings.GetAsync(DirectorySettings.RegistrationDefaultLanguage, cancellationToken);
            var submitted = RegistrationRequest.Submit(
                Guid.CreateVersion7(),
                new RegistrationDetails(
                    request.FirstName, request.LastName, request.Email, request.Phone, request.BirthDate, request.FiscalCode,
                    request.PrivacyConsent, request.PrivacyVersion),
                language,
                client.ClientId,
                await settings.GetAsync(DirectorySettings.RegistrationMinimumAge, cancellationToken),
                clock.GetUtcNow());
            if (submitted.IsFailure)
            {
                return Result.Failure<RegistrationSubmittedResponse>(submitted.Error!);
            }

            // After the field checks (they reveal nothing) and before the duplicate checks (they would reveal an e-mail).
            if (!await verifier.VerifyAsync(request.Captcha, cancellationToken))
            {
                Log.Security.CaptchaRejected(logger, client.CaptchaProvider, client.ClientId);
                return Errors.Directory.RegistrationCaptchaInvalid();
            }

            var registration = submitted.Value;
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.PendingExistsAsync(registration.Email, cancellationToken))
            {
                return Errors.Directory.RegistrationPending();
            }

            if (await store.EmailRegisteredAsync(registration.Email, cancellationToken))
            {
                return Errors.Directory.RegistrationEmailRegistered();
            }

            store.Add(registration);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Registration", registration.Id);

            if (await settings.GetAsync(DirectorySettings.RegistrationSendConfirmationEmail, cancellationToken))
            {
                // A delivery problem is logged by the dispatcher and never fails the request (F02).
                _ = await messages.QueueAsync(
                    new OutboundMessageRequest(
                        MessageChannel.Email, MessagePurpose.Transactional, registration.Email, MessageTemplates.RegistrationReceived, registration.Language,
                        new Dictionary<string, object?> { ["name"] = registration.FirstName, ["appName"] = tenantContext.Tenant.Slug },
                        nameof(RegistrationRequest),
                        registration.Id),
                    cancellationToken);
            }

            if (await settings.GetAsync(DirectorySettings.RegistrationNotifyAdmins, cancellationToken))
            {
                // Persistent notifications arrive with the notification centre (B-19); until then a real-time push.
                var pushed = new RegistrationRequestedEvent(registration.Id, registration.FullName);
                scope.OnCommitted(ct => notifier.ToRoleAsync(TenantRole.Administrator, RealtimeEvents.RegistrationRequested, pushed, ct));
            }

            return new RegistrationSubmittedResponse(registration.Id);
        }, cancellationToken);
    }

    public async Task<Result<ApproveRegistrationResponse>> ApproveAsync(Guid id, string? notes, CancellationToken cancellationToken)
    {
        var approved = await operations.RunAsync<(Guid ClientId, Guid UserId, bool CanSignIn)>(Operations.Directory.ApproveRegistration, new { RegistrationId = id }, async scope =>
        {
            if (currentUser.UserId is not { } processor)
            {
                return Errors.Identity.PermissionDenied();
            }

            await using var registrations = await data.OpenAsync(cancellationToken);
            if (await registrations.FindAsync(id, cancellationToken) is not { } registration)
            {
                return Errors.Directory.RegistrationNotFound();
            }

            var now = clock.GetUtcNow();
            if (registration.Status != RegistrationStatus.Pending)
            {
                return Errors.Directory.RegistrationProcessed();
            }

            if (RegistrationRequest.CheckNotes(notes) is { IsFailure: true } invalidNotes)
            {
                return Result.Failure<(Guid, Guid, bool)>(invalidNotes.Error!);
            }

            // The person rules may be stricter than when the request arrived: checked again.
            var person = Person.Create(Guid.CreateVersion7(), registration.ToPersonDetails(), DateOnly.FromDateTime(now.UtcDateTime));
            if (person.IsFailure)
            {
                return Result.Failure<(Guid, Guid, bool)>(person.Error!);
            }

            Guid? employee;
            await using (var store = await clients.OpenAsync(cancellationToken))
            {
                // Q54: the fiscal code stays unique on approval too.
                if (await store.FiscalCodeTakenAsync(person.Value.FiscalCode!, null, cancellationToken))
                {
                    return Errors.Directory.FiscalCodeTaken();
                }

                employee = await store.DefaultEmployeeAsync(cancellationToken);
                store.Add(person.Value);
                store.Add(ClientProfile.Create(person.Value.Id, employee, now));
                await store.SaveChangesAsync(cancellationToken);
            }

            scope.SetEntity("Client", person.Value.Id);

            // F05: the e-mail is the user name (a taken one is AUX-12xxx and rolls everything back). Q60: sign-in only with an employee.
            var user = await accounts.CreateAsync(person.Value.Id, registration.Email, registration.Email, TenantRole.Client, employee is not null, cancellationToken);
            if (user.IsFailure)
            {
                return Result.Failure<(Guid, Guid, bool)>(user.Error!);
            }

            var processed = registration.Approve(person.Value.Id, processor, notes, now);
            if (processed.IsFailure)
            {
                return Result.Failure<(Guid, Guid, bool)>(processed.Error!);
            }

            await registrations.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Registration", registration.Id);
            return Result.Success((person.Value.Id, user.Value, employee is not null));
        }, cancellationToken);

        if (approved.IsFailure)
        {
            return Result.Failure<ApproveRegistrationResponse>(approved.Error!);
        }

        var (clientId, userId, canSignIn) = approved.Value;
        if (!canSignIn)
        {
            return new ApproveRegistrationResponse(clientId, false, Errors.Directory.ClientEmployeeRequired().DisplayCode);
        }

        // The client exists even when the e-mail cannot leave: the invitation can be sent again from the client.
        var sent = await accounts.SendActivationAsync(userId, cancellationToken);
        if (sent.IsFailure)
        {
            Log.Identity.InvitationPending(logger, userId, sent.Error!.DisplayCode);
            return new ApproveRegistrationResponse(clientId, false, sent.Error.DisplayCode);
        }

        return new ApproveRegistrationResponse(clientId, true, null);
    }

    public Task<Result> RejectAsync(Guid id, string? notes, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.RejectRegistration, new { RegistrationId = id }, async _ =>
        {
            if (currentUser.UserId is not { } processor)
            {
                return Errors.Identity.PermissionDenied();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } registration)
            {
                return Errors.Directory.RegistrationNotFound();
            }

            var rejected = registration.Reject(processor, notes, clock.GetUtcNow());
            if (rejected.IsFailure)
            {
                return rejected;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>The calling client and its captcha verifier, when the tenant accepts registrations.</summary>
    private async Task<Result<(CallingClient Client, ICaptchaVerifier Verifier)>> OpenAsync(RegistrationCaller caller, CancellationToken cancellationToken)
    {
        if (await clientApplications.AuthenticateAsync(caller.ClientId, caller.ClientSecret, cancellationToken) is not { } client)
        {
            return Errors.Identity.ClientInvalid();
        }

        if (!await settings.GetAsync(DirectorySettings.RegistrationEnabled, cancellationToken)
            || !Reviewers.Any((await modules.GetAsync(cancellationToken)).RolesOf(DirectoryModule.ModuleCode).Contains))
        {
            return Errors.Directory.RegistrationDisabled();
        }

        // A provider without an adapter is a configuration error of the client application: refused like an invalid client.
        if (captchas.FirstOrDefault(verifier => verifier.Provider == client.CaptchaProvider) is not { } captcha)
        {
            Log.Security.ClientRejected(logger, client.ClientId, $"captcha provider {client.CaptchaProvider}");
            return Errors.Identity.ClientInvalid();
        }

        return (client, captcha);
    }
}

internal sealed class RegistrationQueryService(IRegistrationDataFactory data) : IRegistrationQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    private static readonly Dictionary<string, RegistrationSort> Sorts = new(StringComparer.Ordinal)
    {
        ["requestedAt"] = RegistrationSort.RequestedAt,
        ["processedAt"] = RegistrationSort.ProcessedAt,
        ["lastName"] = RegistrationSort.LastName,
    };

    public async Task<Result<PagedResponse<RegistrationResponse>>> ListAsync(RegistrationListQuery query, CancellationToken cancellationToken)
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
        var sort = RegistrationSort.RequestedAt;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        RegistrationStatus? status = null;
        if (!string.IsNullOrEmpty(query.Status))
        {
            if (Enum.TryParse<RegistrationStatus>(query.Status, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
            {
                status = parsed;
            }
            else
            {
                errors["status"] = ["validation.registration.status"];
            }
        }

        if (query.From > query.To)
        {
            errors["to"] = ["validation.registration.dateRange"];
        }

        if (query.Search is { Length: > MaxSearchLength })
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new RegistrationFilter(
                status,
                query.From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
                query.To is { } to ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
                string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
                sort,
                query.Sort?.StartsWith('-') == true,
                (query.Page - 1) * query.PageSize,
                query.PageSize),
            cancellationToken);
        return new PagedResponse<RegistrationResponse>(items.Select(ToResponse).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<RegistrationResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.GetAsync(id, cancellationToken) is { } row ? ToResponse(row) : Errors.Directory.RegistrationNotFound();
    }

    private static RegistrationResponse ToResponse(RegistrationRow row)
    {
        var request = row.Request;
        return new RegistrationResponse(
            request.Id,
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone,
            request.BirthDate,
            request.FiscalCode,
            request.Language,
            request.PrivacyVersion,
            request.PrivacyConsentedAt,
            request.ClientApplication,
            request.Status.ToString(),
            request.RequestedAt,
            request.ProcessedAt,
            request.ProcessedByUserId is { } processor ? new RegistrationProcessorResponse(processor, row.ProcessedByName ?? string.Empty) : null,
            request.Notes,
            request.ClientId);
    }
}
