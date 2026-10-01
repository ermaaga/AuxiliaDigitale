using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Abstractions.Validation;
using Auxilia.Application.Identity.Public;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

internal sealed class TenantAdministratorManager : ITenantAdministratorManager
{
    private readonly IOperationRunner operations;
    private readonly IIdentityDataFactory data;
    private readonly IAccountLinkManager links;
    private readonly ITenantContext tenantContext;
    private readonly IValidator<CreateTenantAdministratorRequest> validator;
    private readonly ILogger<TenantAdministratorManager> logger;

    public TenantAdministratorManager(
        IOperationRunner operations,
        IIdentityDataFactory data,
        IAccountLinkManager links,
        ITenantContext tenantContext,
        IValidator<CreateTenantAdministratorRequest> validator,
        ILogger<TenantAdministratorManager> logger)
    {
        this.operations = operations;
        this.data = data;
        this.links = links;
        this.tenantContext = tenantContext;
        this.validator = validator;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<TenantAdministratorResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.UsersWithRoleAsync(TenantRole.Administrator, cancellationToken))
            .Select(user => new TenantAdministratorResponse(user.Id, user.UserName, user.Email, user.IsActive, IsActivated(user)))
            .ToArray();
    }

    public async Task<Result<TenantAdministratorInvitationResponse>> CreateInitialAsync(CreateTenantAdministratorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await operations.RunAsync(Operations.Identity.CreateInitialAdministrator, null, async scope =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToResult<Guid>();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if ((await store.UsersWithRoleAsync(TenantRole.Administrator, cancellationToken)).Count > 0)
            {
                return Errors.Identity.AdministratorAlreadyExists();
            }

            var email = request.Email.Trim();
            if (await store.UserNameExistsAsync(email, cancellationToken))
            {
                return Errors.Identity.UserNameTaken();
            }

            var person = new Person(Guid.CreateVersion7(), request.FirstName, request.LastName, email);
            var language = tenantContext.Current?.DefaultLanguage ?? "it";
            var user = User.Create(Guid.CreateVersion7(), person.Id, email, email, language, [TenantRole.Administrator], isActive: true);
            if (user.IsFailure)
            {
                return Result.Failure<Guid>(user.Error!);
            }

            store.Add(person);
            store.Add(user.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("User", user.Value.Id);
            return Result.Success(user.Value.Id);
        }, cancellationToken);

        // The account exists even when the e-mail cannot leave: the invitation is a separate operation.
        return created.IsFailure
            ? Result.Failure<TenantAdministratorInvitationResponse>(created.Error!)
            : await InviteAsync(created.Value, cancellationToken);
    }

    public async Task<Result<TenantAdministratorInvitationResponse>> SendInvitationAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            var user = await store.FindAsync(userId, cancellationToken);
            if (user is null || !user.Roles.Contains(TenantRole.Administrator))
            {
                return Errors.Identity.UserNotFound();
            }

            if (IsActivated(user))
            {
                return Errors.Identity.AccountAlreadyActivated();
            }
        }

        return await InviteAsync(userId, cancellationToken);
    }

    private static bool IsActivated(User user) => !string.IsNullOrEmpty(user.PasswordHash);

    private async Task<Result<TenantAdministratorInvitationResponse>> InviteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sent = await links.SendActivationAsync(userId, cancellationToken);
        if (sent.IsFailure)
        {
            Log.Identity.InvitationPending(logger, userId, sent.Error!.DisplayCode);
            return new TenantAdministratorInvitationResponse(userId, false, sent.Error.DisplayCode);
        }

        return new TenantAdministratorInvitationResponse(userId, true, null);
    }
}

internal sealed class CreateTenantAdministratorRequestValidator : AbstractValidator<CreateTenantAdministratorRequest>
{
    public CreateTenantAdministratorRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().MaximumLength(Person.EmailMaxLength).EmailAddress();
        RuleFor(request => request.FirstName).NotEmpty().MaximumLength(Person.NameMaxLength);
        RuleFor(request => request.LastName).NotEmpty().MaximumLength(Person.NameMaxLength);
    }
}
