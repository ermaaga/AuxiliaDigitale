using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Localization.Public;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Identity;

/// <summary>
/// The own profile of the signed-in tenant user (F04, Q35): personal data, language, theme, picture and own sessions.
/// The password change is <see cref="ISessionManager.ChangePasswordAsync"/> (current password required). Every method
/// acts on the caller only; a platform token or a missing account is <c>AUX-12006</c>.
/// </summary>
public interface IProfileManager
{
    /// <summary>F04: first and last name, e-mail (required) and phone; the user name never changes here.</summary>
    Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken);

    /// <summary>An active language of the tenant; used by the UI at once and for the next sign-ins.</summary>
    Task<Result> ChangeLanguageAsync(string? languageCode, CancellationToken cancellationToken);

    /// <summary>Q35: the colour scheme (<c>System</c>, <c>Light</c>, <c>Dark</c>), kept with the account.</summary>
    Task<Result> UpdatePreferencesAsync(UpdatePreferencesRequest request, CancellationToken cancellationToken);

    /// <summary>F04: a JPEG, PNG or WebP picture of at most 2 MB, stored resized to at most 400 × 400 (JPEG).</summary>
    Task<Result> SetImageAsync(byte[] content, CancellationToken cancellationToken);

    Task<Result> RemoveImageAsync(CancellationToken cancellationToken);
}

/// <summary>The stored picture of a user, for <c>GET /users/{id}/image</c> (ETag = hash).</summary>
public sealed record ProfileImageContent(byte[] Content, string ContentType, string Hash);

public interface IProfileQueryService
{
    Task<Result<ProfileResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>The open sessions of the caller, most recently used first; <paramref name="currentSessionId"/> is marked.</summary>
    Task<Result<IReadOnlyList<MySessionResponse>>> SessionsAsync(Guid? currentSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// The picture of a user: the own one for everybody, any user's for staff (Administrator, Employee: grids and
    /// details). Otherwise, and when there is no picture, not found.
    /// </summary>
    Task<Result<ProfileImageContent>> ImageAsync(Guid userId, CancellationToken cancellationToken);
}

internal sealed class ProfileManager(
    IOperationRunner operations,
    IProfileDataFactory data,
    ITenantLanguages languages,
    IImageProcessor images,
    ICurrentUser currentUser,
    TimeProvider clock) : IProfileManager
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RunAsync(Operations.Identity.UpdateProfile, async (store, user) =>
        {
            if (await store.FindPersonAsync(user.PersonId, cancellationToken) is not { } person)
            {
                return Errors.Identity.UserNotFound();
            }

            // Birth date and fiscal code are staff data (F05, F06): they stay as they are.
            var details = new PersonDetails(request.FirstName, request.LastName, request.Email, person.BirthDate, request.Phone, person.FiscalCode);
            var updated = person.Update(details, Today);
            var errors = new Dictionary<string, string[]>(
                updated.IsFailure ? updated.Error!.ValidationErrors : new Dictionary<string, string[]>(), StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                // The e-mail receives the reset and sign-in codes (F01, F35).
                errors.TryAdd("email", ["validation.person.email"]);
            }

            if (errors.Count > 0)
            {
                return Errors.Directory.PersonInvalid(errors);
            }

            var account = user.ChangeAccount(user.UserName, person.Email);
            if (account.IsFailure)
            {
                return account;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> ChangeLanguageAsync(string? languageCode, CancellationToken cancellationToken) =>
        RunAsync(Operations.Identity.ChangeLanguage, async (store, user) =>
        {
            var code = languageCode?.Trim();
            if (!await languages.IsActiveAsync(code, cancellationToken))
            {
                return Errors.Identity.LanguageNotAvailable();
            }

            var changed = user.ChangeLanguage(code!);
            if (changed.IsSuccess)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return changed;
        }, cancellationToken);

    public Task<Result> UpdatePreferencesAsync(UpdatePreferencesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RunAsync(Operations.Identity.ChangePreferences, async (store, user) =>
        {
            // Names only (System, Light, Dark): numbers are not themes.
            if (string.IsNullOrEmpty(request.Theme)
                || char.IsDigit(request.Theme[0])
                || !Enum.TryParse<UserTheme>(request.Theme, ignoreCase: false, out var theme)
                || !Enum.IsDefined(theme))
            {
                return Errors.Identity.UserValueInvalid("theme");
            }

            var changed = user.ChangeTheme(theme);
            await store.SaveChangesAsync(cancellationToken);
            return changed;
        }, cancellationToken);
    }

    public Task<Result> SetImageAsync(byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        return RunAsync(Operations.Identity.SetProfileImage, async (store, user) =>
        {
            if (content.Length is 0 or > UserImage.UploadMaxBytes || images.ResizeToJpeg(content, UserImage.MaxSide) is not { } resized)
            {
                return Errors.Identity.ProfileImageInvalid();
            }

            if (await store.FindImageAsync(user.Id, cancellationToken) is { } image)
            {
                image.Replace(resized.Content, resized.ContentType);
            }
            else
            {
                store.Add(UserImage.Create(user.Id, resized.Content, resized.ContentType));
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> RemoveImageAsync(CancellationToken cancellationToken) =>
        RunAsync(Operations.Identity.RemoveProfileImage, async (store, user) =>
        {
            if (await store.FindImageAsync(user.Id, cancellationToken) is not { } image)
            {
                return Errors.Identity.ProfileImageNotFound();
            }

            store.Remove(image);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>Runs <paramref name="work"/> on the caller's account inside the operation.</summary>
    private Task<Result> RunAsync(OperationDescriptor operation, Func<IProfileData, User, Task<Result>> work, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorType == ActorType.User ? currentUser.UserId : null;
        return operations.RunAsync(operation, new { UserId = userId }, async _ =>
        {
            if (userId is not { } id)
            {
                return Errors.Identity.UserNotFound();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            return await store.FindUserAsync(id, cancellationToken) is { IsActive: true } user
                ? await work(store, user)
                : Errors.Identity.UserNotFound();
        }, cancellationToken);
    }
}

internal sealed class ProfileQueryService(
    IProfileDataFactory data,
    ISessionDataFactory sessions,
    ICurrentUser currentUser,
    TimeProvider clock) : IProfileQueryService
{
    private Guid? CallerId => currentUser.ActorType == ActorType.User ? currentUser.UserId : null;

    public async Task<Result<ProfileResponse>> GetAsync(CancellationToken cancellationToken)
    {
        if (CallerId is not { } userId)
        {
            return Errors.Identity.UserNotFound();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user
            || await store.FindPersonAsync(user.PersonId, cancellationToken) is not { } person)
        {
            return Errors.Identity.UserNotFound();
        }

        var image = await store.FindImageInfoAsync(userId, cancellationToken);
        return new ProfileResponse(
            user.Id, user.UserName, person.FirstName, person.LastName, person.Email, person.Phone, user.LanguageCode, user.Theme.ToString(), image?.Hash);
    }

    public async Task<Result<IReadOnlyList<MySessionResponse>>> SessionsAsync(Guid? currentSessionId, CancellationToken cancellationToken)
    {
        if (CallerId is not { } userId)
        {
            return Errors.Identity.UserNotFound();
        }

        var now = clock.GetUtcNow();
        await using var store = await sessions.OpenAsync(cancellationToken);
        return (await store.OpenSessionsOfUserAsync(userId, cancellationToken))
            .Where(session => session.IsActiveAt(now))
            .OrderByDescending(session => session.LastUsedAt)
            .Select(session => new MySessionResponse(
                session.Id,
                session.ClientId,
                session.CreatedAt,
                session.LastUsedAt,
                session.IdleExpiresAt < session.AbsoluteExpiresAt ? session.IdleExpiresAt : session.AbsoluteExpiresAt,
                session.IpAddress,
                session.UserAgent,
                session.Id == currentSessionId))
            .ToArray();
    }

    public async Task<Result<ProfileImageContent>> ImageAsync(Guid userId, CancellationToken cancellationToken)
    {
        var isStaff = currentUser.Roles.Contains(TenantRole.Administrator) || currentUser.Roles.Contains(TenantRole.Employee);
        if (CallerId is not { } callerId || (callerId != userId && !isStaff))
        {
            return Errors.Identity.ProfileImageNotFound();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindImageAsync(userId, cancellationToken) is { } image
            ? new ProfileImageContent(image.Content, image.ContentType, image.Hash)
            : Errors.Identity.ProfileImageNotFound();
    }
}
