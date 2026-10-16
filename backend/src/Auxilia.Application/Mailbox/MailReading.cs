using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Cases.Public;
using Auxilia.Application.Documents.Public;
using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Mailbox;

/// <summary>A file of a message to download (<c>.eml</c> or attachment): a temporary stream, closed by the caller.</summary>
public sealed record MailFile(Stream Content, string FileName, string ContentType);

/// <summary>
/// A message of the caller's own mailbox read live from the provider (N05, C-06b): body and attachments, the original
/// <c>.eml</c> and each attachment. Nothing of the body is stored.
/// </summary>
public interface IMailMessageQueryService
{
    Task<Result<MailMessageDetailResponse>> ReadAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The original message (RFC 822): a PEC keeps receipt and signature.</summary>
    Task<Result<MailFile>> OpenEmlAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<MailFile>> OpenAttachmentAsync(Guid id, int index, CancellationToken cancellationToken);
}

/// <summary>"File into case" (N05, C-06b): the <c>.eml</c> and the attachments chosen become documents of the case.</summary>
public interface IMailMessageManager
{
    Task<Result<FileMailResponse>> FileAsync(Guid id, FileMailRequest request, CancellationToken cancellationToken);
}

/// <summary>A stored message of the caller with its original read from the provider (a temporary stream).</summary>
internal sealed record LiveMessage(OwnedMailMessage Owned, Stream Raw) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Raw.DisposeAsync();
}

/// <summary>
/// Opens the original of a stored message of the caller: the account must work (a refused grant or password marks it
/// to reconnect), the provider is asked live, the size is bounded. Provider failures become error codes for the page.
/// </summary>
internal sealed class LiveMessages(
    IOperationRunner operations,
    IMailboxSyncDataFactory data,
    IEnumerable<IMailProvider> mails,
    MailboxAccessResolver accessResolver,
    IPermissionAccess permissions,
    ConnectedAccountPolicy policy,
    TimeProvider clock,
    ILogger<LiveMessages> logger)
{
    public async Task<Result<OwnedMailMessage>> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ViewMail, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindMessageOfUserAsync(me, id, cancellationToken) is { } owned ? owned : Errors.Mailbox.MailMessageNotFound();
    }

    public async Task<Result<LiveMessage>> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<LiveMessage>(found.Error!);
        }

        var owned = found.Value;
        var account = owned.Account;
        if (account.Status != ConnectedAccountStatus.Active)
        {
            return Errors.Mailbox.SyncCredentialsRefused();
        }

        if (mails.FirstOrDefault(mail => mail.Provider == account.Provider) is not { } mail)
        {
            return Errors.Mailbox.ProviderNotAvailable(ProviderNames.Of(account.Provider));
        }

        Stream? raw;
        try
        {
            var access = await accessResolver.ResolveAsync(account, cancellationToken);
            if (access.IsFailure)
            {
                return access.Error!.Code == EventCodes.Mailbox.SyncCredentialsRefused
                    ? await RefusedAsync(account.Id, cancellationToken)
                    : Result.Failure<LiveMessage>(access.Error);
            }

            var folder = new MailFolderData(owned.Folder.ProviderFolderId, owned.Folder.Name, owned.Folder.Kind);
            raw = await mail.OpenRawAsync(access.Value, folder, owned.Message.ProviderMessageId, cancellationToken);
        }
        catch (MailboxProviderException exception) when (exception.Failure == ProviderFailure.Credentials)
        {
            return await RefusedAsync(account.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is MailboxProviderException or System.Net.Sockets.SocketException
            || AccountSyncService.IsTransient(exception, cancellationToken))
        {
            var (providerName, reason) = (ProviderNames.Of(account.Provider), exception is MailboxProviderException classified ? classified.Reason : exception.GetType().Name);
            Log.Mailbox.MailReadFailed(logger, exception, id, account.Id, providerName, reason);
            return Errors.Mailbox.SyncProviderUnavailable();
        }

        if (raw is null)
        {
            return Errors.Mailbox.MailMessageNotFound();
        }

        if (raw.Length > MailRaw.MaxBytes)
        {
            await raw.DisposeAsync();
            return Errors.Mailbox.MailMessageTooLarge();
        }

        raw.Position = 0;
        return new LiveMessage(owned, raw);
    }

    /// <summary>The key of a message across folders and accounts: its <c>Message-ID</c> (or the provider id without one), hashed.</summary>
    public static string KeyHash(OwnedMailMessage owned) =>
        Hash(owned.Message.InternetMessageId is { Length: > 0 } messageId
            ? messageId.Trim().ToLowerInvariant()
            : $"{owned.Account.Id}:{owned.Folder.Kind}:{owned.Message.ProviderMessageId}");

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>The name of the <c>.eml</c>: day received and subject, without characters a file name cannot hold.</summary>
    public static string EmlFileName(MailMessage message)
    {
        var subject = new string([.. message.Subject.Select(character =>
            char.IsControl(character) || Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
                ? ' '
                : character)]).Trim().TrimEnd('.');
        subject = string.Join(' ', subject.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (subject.Length > 80)
        {
            subject = subject[..80].TrimEnd();
        }

        var day = message.ReceivedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return subject.Length > 0 ? $"{day} {subject}.eml" : $"{day} message.eml";
    }

    /// <summary>The provider refused the stored grant or password: the account is marked to reconnect (the owner is on the page).</summary>
    private async Task<Result<LiveMessage>> RefusedAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var code = Errors.Mailbox.SyncCredentialsRefused();
        await operations.RunAsync(Operations.Mailbox.UpdateAccount, new { ConnectedAccountId = accountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAccountAsync(accountId, cancellationToken) is { Status: ConnectedAccountStatus.Active } account)
            {
                account.MarkNeedsReconnect(code.DisplayCode, clock.GetUtcNow());
                var providerName = ProviderNames.Of(account.Provider);
                Log.Mailbox.MailReadFailed(logger, null, Guid.Empty, account.Id, providerName, "CredentialsRefused");
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);
        return code;
    }
}

internal sealed class MailMessageQueryService(
    LiveMessages live,
    IMailContentReader reader,
    IMailboxSyncDataFactory data,
    ICaseDirectory cases) : IMailMessageQueryService
{
    public async Task<Result<MailMessageDetailResponse>> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var opened = await live.OpenAsync(id, cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<MailMessageDetailResponse>(opened.Error!);
        }

        await using var message = opened.Value;
        var content = await reader.ReadAsync(message.Raw, MailRaw.MaxBodyChars, cancellationToken);
        return new MailMessageDetailResponse(
            MailboxSyncQueryService.ToResponse(message.Owned.Message),
            content.Text,
            content.Html,
            content.Truncated,
            [.. content.Attachments.Select(item => new MailAttachmentResponse(item.Index, item.FileName, item.ContentType, item.Size))],
            await FiledInAsync(message.Owned, cancellationToken));
    }

    public async Task<Result<MailFile>> OpenEmlAsync(Guid id, CancellationToken cancellationToken)
    {
        var opened = await live.OpenAsync(id, cancellationToken);
        return opened.IsFailure
            ? Result.Failure<MailFile>(opened.Error!)
            : new MailFile(opened.Value.Raw, LiveMessages.EmlFileName(opened.Value.Owned.Message), "message/rfc822");
    }

    public async Task<Result<MailFile>> OpenAttachmentAsync(Guid id, int index, CancellationToken cancellationToken)
    {
        if (index < 0)
        {
            return Errors.Mailbox.MailAttachmentNotFound();
        }

        var opened = await live.OpenAsync(id, cancellationToken);
        if (opened.IsFailure)
        {
            return Result.Failure<MailFile>(opened.Error!);
        }

        await using var message = opened.Value;
        return await reader.OpenAttachmentAsync(message.Raw, index, cancellationToken) is { } attachment
            ? new MailFile(attachment.Content, attachment.Info.FileName, attachment.Info.ContentType)
            : Errors.Mailbox.MailAttachmentNotFound();
    }

    /// <summary>The cases the message is filed into that the caller may see (F10), once each, newest filing.</summary>
    private async Task<IReadOnlyList<FiledCaseResponse>> FiledInAsync(OwnedMailMessage owned, CancellationToken cancellationToken)
    {
        IReadOnlyList<FiledMessage> filings;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            filings = await store.FilingsAsync(LiveMessages.KeyHash(owned), cancellationToken);
        }

        var result = new List<FiledCaseResponse>();
        foreach (var filing in filings.GroupBy(item => item.CaseId).Select(group => group.MaxBy(item => item.FiledAt)!))
        {
            if (await cases.FindAsync(filing.CaseId, cancellationToken) is { CanSee: true } found)
            {
                result.Add(new FiledCaseResponse(found.Id, found.Number, filing.FiledAt));
            }
        }

        return result;
    }
}

internal sealed class MailMessageManager(
    IOperationRunner operations,
    LiveMessages live,
    IMailContentReader reader,
    IMailboxSyncDataFactory data,
    ICaseDocuments documents,
    IAccessGuard guard,
    ConnectedAccountPolicy policy,
    TimeProvider clock) : IMailMessageManager
{
    /// <summary>Attachments filed with one message at most (the documents upload takes 50 files, the <c>.eml</c> included).</summary>
    public const int MaxAttachments = 49;

    public Task<Result<FileMailResponse>> FileAsync(Guid id, FileMailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<FileMailResponse>(Operations.Mailbox.FileMessage, new { MailMessageId = id, request.CaseId }, async scope =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.FileMail, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<FileMailResponse>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            var attachments = request.Attachments ?? [];
            if (attachments.Count > MaxAttachments || attachments.Any(index => index < 0) || attachments.Distinct().Count() != attachments.Count)
            {
                return Errors.Mailbox.MailFilingInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["attachments"] = ["validation.mailbox.attachments"] });
            }

            var found = await live.FindAsync(id, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<FileMailResponse>(found.Error!);
            }

            var keyHash = LiveMessages.KeyHash(found.Value);
            if (!request.Force)
            {
                await using var store = await data.OpenAsync(cancellationToken);
                if ((await store.FilingsAsync(keyHash, cancellationToken)).Any(filing => filing.CaseId == request.CaseId))
                {
                    return Errors.Mailbox.MailAlreadyFiled();
                }
            }

            var opened = await live.OpenAsync(id, cancellationToken);
            if (opened.IsFailure)
            {
                return Result.Failure<FileMailResponse>(opened.Error!);
            }

            await using var message = opened.Value;
            var files = new List<CaseFile>();
            try
            {
                foreach (var index in attachments)
                {
                    message.Raw.Position = 0;
                    if (await reader.OpenAttachmentAsync(message.Raw, index, cancellationToken) is not { } attachment)
                    {
                        return Errors.Mailbox.MailFilingInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["attachments"] = ["validation.mailbox.attachments"] });
                    }

                    files.Add(new CaseFile(attachment.Content, attachment.Info.FileName));
                }

                message.Raw.Position = 0;
                files.Insert(0, new CaseFile(message.Raw, LiveMessages.EmlFileName(message.Owned.Message)));
                var filed = await documents.FileAsync(new CaseFiling(request.CaseId, request.FolderId, request.Description, files), cancellationToken);
                if (filed.IsFailure)
                {
                    return Result.Failure<FileMailResponse>(filed.Error!);
                }

                await using (var store = await data.OpenAsync(cancellationToken))
                {
                    store.Add(FiledMessage.Record(
                        Guid.CreateVersion7(), keyHash, LiveMessages.Hash(message.Owned.Account.Address), request.CaseId, filed.Value[0], me, clock.GetUtcNow()));
                    await store.SaveChangesAsync(cancellationToken);
                }

                scope.SetEntity("Case", request.CaseId);
                return new FileMailResponse(filed.Value[0], [.. filed.Value.Skip(1)]);
            }
            finally
            {
                foreach (var file in files.Where(file => !ReferenceEquals(file.Content, message.Raw)))
                {
                    await file.Content.DisposeAsync();
                }
            }
        }, cancellationToken);
    }
}
