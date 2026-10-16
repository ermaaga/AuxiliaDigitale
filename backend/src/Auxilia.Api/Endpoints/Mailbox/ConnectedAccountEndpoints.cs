using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.RateLimiting;
using Auxilia.Application.Mailbox;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Mailbox;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Auxilia.Api.Endpoints.Mailbox;

/// <summary>
/// The caller's own connected accounts (N05, ADR 0021): list, providers offered, "Sign in with Google / Microsoft"
/// started and completed through the BFF, PEC / IMAP with a connection test, colour, disconnect. Another user's account
/// is 404. Module <c>mailbox</c>: 404 when not visible to the role.
/// </summary>
internal sealed class ConnectedAccountEndpoints : IModuleEndpoints
{
    public string ModuleCode => MailboxModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var accounts = module.MapGroup("/me/connected-accounts").WithTags("Connected accounts");

        accounts.MapGet("/", ListAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .WithName("ListConnectedAccounts")
            .WithSummary("My connected accounts (Google, Microsoft, PEC / IMAP), oldest first; secrets are never returned")
            .Produces<IReadOnlyList<ConnectedAccountResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        accounts.MapGet("/providers", ProvidersAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .WithName("ListConnectedAccountProviders")
            .WithSummary("Providers I can connect: Google and Microsoft only when this deployment has their OAuth application; IMAP always")
            .Produces<IReadOnlyList<ConnectedAccountProviderResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        accounts.MapGet("/imap-server", SuggestImapServerAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .WithName("SuggestImapServer")
            .WithSummary("The IMAP server of a well-known provider (PEC included) from the domain of the address; nulls when unknown")
            .Produces<ImapServerSuggestionResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        accounts.MapPost("/oauth/{provider}/authorize", StartOAuthAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .RequireRateLimiting(RateLimitingSetup.ConnectedAccountsPolicy)
            .WithName("StartConnectedAccountOAuth")
            .WithSummary("Starts \"Sign in with Google / Microsoft\" (provider google or microsoft): the consent page to send the browser to, valid 10 minutes")
            .Produces<StartOAuthResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        accounts.MapPost("/oauth/{provider}/callback", CompleteOAuthAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .RequireRateLimiting(RateLimitingSetup.ConnectedAccountsPolicy)
            .WithName("CompleteConnectedAccountOAuth")
            .WithSummary("Completes the consent with what the provider sent back (state + code, or error): connects or reconnects the account")
            .Produces<ConnectedAccountResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        accounts.MapPost("/imap", ConnectImapAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .RequireRateLimiting(RateLimitingSetup.ConnectedAccountsPolicy)
            .WithName("ConnectImapAccount")
            .WithSummary("Connects a PEC / IMAP account after a connection test (TLS, public server address); the same address again replaces password and server")
            .Produces<ConnectedAccountResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        accounts.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .WithName("UpdateConnectedAccount")
            .WithSummary("Changes the colour of one of my accounts")
            .Produces<ConnectedAccountResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapDelete("/{id:guid}", DisconnectAsync)
            .RequirePermission(MailboxPermissions.ConnectAccounts)
            .WithName("DisconnectConnectedAccount")
            .WithSummary("Disconnects one of my accounts: deletes its secret and copied data (documents filed in cases stay) and revokes the Google grant")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/sync", StartSyncAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .RequireRateLimiting(RateLimitingSetup.MailboxSyncPolicy)
            .WithName("StartMailboxSync")
            .WithSummary("Synchronises my accounts with mail or calendar (page opened, or \"Sync now\" with manual): a run per account, queued for the Worker; a run in progress or ended less than mailbox.sync.minIntervalSeconds ago is returned instead")
            .Produces<SyncRequestResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        accounts.MapGet("/sync", SyncStatusAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("GetMailboxSyncStatus")
            .WithSummary("The newest sync run of each of my accounts for the scope (mail or calendar)")
            .Produces<SyncRequestResponse>()
            .ProducesValidationProblem();

        accounts.MapGet("/calendar-events", CalendarEventsAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("ListConnectedCalendarEvents")
            .WithSummary("The events of my connected calendars in a range of local days (at most 62), read only")
            .Produces<CalendarEventsResponse>()
            .ProducesValidationProblem();

        accounts.MapGet("/mail-folders", MailFoldersAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("ListConnectedMailFolders")
            .WithSummary("The inbox and sent folders of my connected mailboxes, with their stored message and unread counts")
            .Produces<IReadOnlyList<MailFolderResponse>>();

        accounts.MapGet("/mail-messages", MailMessagesAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("ListConnectedMailMessages")
            .WithSummary("The stored headers of my mail, newest first: one account or folder, search in sender, recipients and subject, unread only (bodies stay at the provider)")
            .Produces<PagedResponse<MailMessageResponse>>()
            .ProducesValidationProblem();

        accounts.MapGet("/mail-messages/{id:guid}", MailMessageAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("GetConnectedMailMessage")
            .WithSummary("A message of my mailbox read live from the provider: bodies (untrusted content: HTML only in a sandboxed frame), attachments and the cases it was already filed into")
            .Produces<MailMessageDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        accounts.MapGet("/mail-messages/{id:guid}/eml", MailEmlAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("DownloadConnectedMailMessage")
            .WithSummary("The original message (.eml, RFC 822) read live from the provider: a PEC keeps receipt and signature")
            .Produces(StatusCodes.Status200OK, contentType: "message/rfc822")
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapGet("/mail-messages/{id:guid}/attachments/{index:int}", MailAttachmentAsync)
            .RequirePermission(MailboxPermissions.ViewMail)
            .WithName("DownloadConnectedMailAttachment")
            .WithSummary("One attachment of a message (always downloaded, never shown inline)")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        accounts.MapPost("/mail-messages/{id:guid}/file", FileMailAsync)
            .RequirePermission(MailboxPermissions.FileMail)
            .WithName("FileConnectedMailMessage")
            .WithSummary("Files a message into a case (F10 rules of the case): the .eml and the attachments chosen become documents; 409 when the case has it already (force to file it again)")
            .Produces<FileMailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> MailMessageAsync(Guid id, IMailMessageQueryService query, CancellationToken cancellationToken) =>
        (await query.ReadAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> MailEmlAsync(Guid id, IMailMessageQueryService query, HttpContext context, CancellationToken cancellationToken) =>
        (await query.OpenEmlAsync(id, cancellationToken)).ToHttpResult(file => Download(context, file, file.ContentType));

    private static async Task<IResult> MailAttachmentAsync(Guid id, int index, IMailMessageQueryService query, HttpContext context, CancellationToken cancellationToken) =>
        (await query.OpenAttachmentAsync(id, index, cancellationToken)).ToHttpResult(file => Download(context, file, "application/octet-stream"));

    private static async Task<IResult> FileMailAsync(Guid id, FileMailRequest request, IMailMessageManager manager, CancellationToken cancellationToken) =>
        (await manager.FileAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.Ok);

    /// <summary>Third-party files: never cached, always an attachment, content type never sniffed (attachments as octet-stream).</summary>
    private static Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult Download(HttpContext context, MailFile file, string contentType)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(file.FileName);
        context.Response.Headers.ContentDisposition = disposition.ToString();
        return TypedResults.Stream(file.Content, contentType);
    }

    private static async Task<IResult> MailFoldersAsync(IMailboxSyncQueryService query, CancellationToken cancellationToken) =>
        (await query.MailFoldersAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> MailMessagesAsync(
        IMailboxSyncQueryService query,
        Guid? accountId,
        Guid? folderId,
        string? search,
        [FromQuery(Name = "filter[unread]")] bool? unread,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await query.MailMessagesAsync(new MailMessageListQuery(accountId, folderId, search, unread, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> StartSyncAsync(StartSyncRequest request, IAccountSyncRequester requester, CancellationToken cancellationToken) =>
        (await requester.RequestAsync(request, cancellationToken)).ToHttpResult(value => TypedResults.Accepted((string?)null, value));

    private static async Task<IResult> SyncStatusAsync(string? scope, IMailboxSyncQueryService query, CancellationToken cancellationToken) =>
        (await query.SyncStatusAsync(scope, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CalendarEventsAsync(DateOnly from, DateOnly to, IMailboxSyncQueryService query, CancellationToken cancellationToken) =>
        (await query.CalendarEventsAsync(from, to, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ListAsync(IConnectedAccountQueryService query, CancellationToken cancellationToken) =>
        (await query.ListAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ProvidersAsync(IConnectedAccountQueryService query, CancellationToken cancellationToken) =>
        (await query.ProvidersAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SuggestImapServerAsync(string? address, IConnectedAccountQueryService query, CancellationToken cancellationToken) =>
        (await query.SuggestImapServerAsync(address, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> StartOAuthAsync(string provider, StartOAuthRequest request, IConnectedAccountManager manager, CancellationToken cancellationToken) =>
        (await manager.StartOAuthAsync(provider, request, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CompleteOAuthAsync(string provider, CompleteOAuthRequest request, IConnectedAccountManager manager, CancellationToken cancellationToken) =>
        (await manager.CompleteOAuthAsync(provider, request, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ConnectImapAsync(ConnectImapRequest request, IConnectedAccountManager manager, CancellationToken cancellationToken) =>
        (await manager.ConnectImapAsync(request, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UpdateAsync(Guid id, UpdateConnectedAccountRequest request, IConnectedAccountManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> DisconnectAsync(Guid id, IConnectedAccountManager manager, CancellationToken cancellationToken) =>
        (await manager.DisconnectAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);
}
