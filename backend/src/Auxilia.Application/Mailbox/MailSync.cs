using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Mailbox;

/// <summary>
/// The headers of the inbox and the sent mail of an account (N05, #100), folder after folder: a first (or full) sync
/// copies the newest messages of the last <c>mailbox.sync.initialDays</c>, at most <c>mailbox.sync.initialMaxMessages</c>
/// per folder, then incremental changes from the provider's token (Gmail <c>historyId</c>, Graph <c>deltaLink</c>, IMAP
/// UIDVALIDITY + last UID). Each chunk saves headers and cursor in one transaction; bodies are never copied. Messages
/// new in an incremental sync are counted as arrived (the owner is notified at the end of the run).
/// </summary>
internal sealed class MailSynchronizer(
    IOperationRunner operations,
    IMailboxSyncDataFactory data,
    IEnumerable<IMailProvider> mails,
    ISettingsProvider settings,
    TimeProvider clock,
    ILogger<MailSynchronizer> logger) : IScopeSynchronizer
{
    public const int PageSize = 100;

    public SyncScope Scope => SyncScope.Mail;

    public bool Supports(ConnectedAccountProvider provider) => mails.Any(mail => mail.Provider == provider);

    public async Task<ChunkOutcome> RunChunkAsync(SyncWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var mail = mails.First(item => item.Provider == work.Provider);
        if (work.MailFolders is null)
        {
            var folders = (await mail.FoldersAsync(work.Access, cancellationToken))
                .Where(MailFolder.IsUsable)
                .GroupBy(folder => folder.Kind)
                .Select(group => group.First())
                .OrderBy(folder => folder.Kind)
                .ToArray();
            await SaveFoldersAsync(work, folders, cancellationToken);
            work.MailFolders = new Queue<MailFolderData>(folders);
        }

        if (!work.MailFolders.TryPeek(out var folder))
        {
            return ChunkOutcome.Done;
        }

        var key = MailFolder.KeyOf(folder.Kind);
        SyncCursor? cursor;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            cursor = await store.FindCursorAsync(work.AccountId, Scope, key, cancellationToken);
        }

        var since = work.StartedAt.AddDays(-await settings.GetAsync(MailboxSettings.SyncInitialDays, cancellationToken));
        var maxMessages = await settings.GetAsync(MailboxSettings.SyncInitialMaxMessages, cancellationToken);
        MailPage page;
        try
        {
            page = await mail.ReadAsync(
                new MailPageRequest(work.Access, folder, cursor?.PageToken, cursor?.SyncToken, since, maxMessages, PageSize), cancellationToken);
        }
        catch (MailboxProviderException exception) when (exception.Failure == ProviderFailure.CursorExpired && work.ResetFolders.Add(key))
        {
            var scopeName = SyncNames.Of(Scope);
            Log.Mailbox.SyncCursorReset(logger, scopeName, work.AccountId, exception.Reason);
            await ResetAsync(work, key, cancellationToken);
            return ChunkOutcome.More;
        }

        var saved = await operations.RunAsync<bool?>(Operations.Mailbox.SaveSyncChunk, new { SyncRunId = work.RunId, ConnectedAccountId = work.AccountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var run = await store.FindRunAsync(work.RunId, cancellationToken);
            var stored = (await store.FoldersAsync(work.AccountId, cancellationToken)).FirstOrDefault(item => item.Kind == folder.Kind);
            if (run is not { Status: SyncRunStatus.Running } || stored is null)
            {
                // Disconnected meanwhile (cascade) or ended elsewhere: nothing is saved.
                return Result.Success<bool?>(null);
            }

            var now = clock.GetUtcNow();
            var current = await store.FindCursorAsync(work.AccountId, Scope, key, cancellationToken);
            if (current is null)
            {
                current = SyncCursor.Begin(work.AccountId, Scope, key, now);
                store.Add(current);
            }

            var incremental = !current.IsFullSync;
            var distinct = page.Messages
                .Where(item => item is not null && !string.IsNullOrEmpty(item.ProviderMessageId))
                .GroupBy(item => item.ProviderMessageId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var usable = distinct.Where(MailMessage.IsUsable).ToArray();
            var withoutId = page.Messages.Count(item => item is null || string.IsNullOrEmpty(item.ProviderMessageId));
            var skipped = page.Malformed + withoutId + (distinct.Length - usable.Length);
            var existing = await store.MessagesAsync(stored.Id, [.. usable.Select(item => item.ProviderMessageId)], cancellationToken);
            var (added, updated) = (0, 0);
            foreach (var item in usable)
            {
                if (existing.TryGetValue(item.ProviderMessageId, out var message))
                {
                    updated += message.Apply(item, now) ? 1 : 0;
                }
                else
                {
                    store.Add(MailMessage.Create(Guid.CreateVersion7(), work.AccountId, stored.Id, item, now));
                    added++;
                }
            }

            var removed = page.RemovedIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            if (removed.Length > 0)
            {
                updated += await store.DeleteMessagesAsync(stored.Id, removed, cancellationToken);
            }

            if (page.Present is { } present)
            {
                updated += await store.DeleteMessagesNotInAsync(stored.Id, present, cancellationToken);
            }

            run.Record(added, updated, skipped, now);
            run.RecordArrived(incremental ? added : 0);
            if (skipped > 0)
            {
                var scopeName = SyncNames.Of(Scope);
                Log.Mailbox.SyncItemsSkipped(logger, skipped, scopeName, work.RunId);
            }

            bool folderDone;
            if (!string.IsNullOrEmpty(page.NextPageToken))
            {
                current.Advance(page.NextPageToken, now);
                folderDone = false;
            }
            else
            {
                if (current.IsFullSync)
                {
                    // The full sync saw every message it keeps: those it did not see are gone (or out of its bounds).
                    run.Record(0, await store.DeleteStaleMessagesAsync(stored.Id, current.FullSyncStartedAt, cancellationToken), 0, now);
                }

                if (string.IsNullOrEmpty(page.SyncToken))
                {
                    current.Reset(now);
                }
                else
                {
                    current.Complete(page.SyncToken, now);
                }

                folderDone = true;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success<bool?>(folderDone);
        }, cancellationToken);

        if (saved.IsFailure)
        {
            throw new InvalidOperationException(saved.Error!.Description);
        }

        switch (saved.Value)
        {
            case null:
                return ChunkOutcome.Done;
            case true:
                work.MailFolders.Dequeue();
                return work.MailFolders.Count > 0 ? ChunkOutcome.More : ChunkOutcome.Done;
            default:
                return ChunkOutcome.More;
        }
    }

    /// <summary>The folders of the account as the provider names them now (a renamed folder keeps its messages and cursor).</summary>
    private async Task SaveFoldersAsync(SyncWork work, IReadOnlyList<MailFolderData> folders, CancellationToken cancellationToken)
    {
        var saved = await operations.RunAsync(Operations.Mailbox.SaveSyncChunk, new { SyncRunId = work.RunId, ConnectedAccountId = work.AccountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindRunAsync(work.RunId, cancellationToken) is not { Status: SyncRunStatus.Running })
            {
                return Result.Success();
            }

            var now = clock.GetUtcNow();
            var existing = (await store.FoldersAsync(work.AccountId, cancellationToken)).ToDictionary(folder => folder.Kind);
            foreach (var folder in folders)
            {
                if (existing.TryGetValue(folder.Kind, out var stored))
                {
                    stored.Apply(folder, now);
                }
                else
                {
                    store.Add(MailFolder.Create(Guid.CreateVersion7(), work.AccountId, folder, now));
                }
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

        if (saved.IsFailure)
        {
            throw new InvalidOperationException(saved.Error!.Description);
        }
    }

    private Task<Result> ResetAsync(SyncWork work, string key, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.SaveSyncChunk, new { SyncRunId = work.RunId, ConnectedAccountId = work.AccountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            (await store.FindCursorAsync(work.AccountId, Scope, key, cancellationToken))?.Reset(clock.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
}
