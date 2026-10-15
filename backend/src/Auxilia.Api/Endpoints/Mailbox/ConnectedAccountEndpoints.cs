using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.RateLimiting;
using Auxilia.Application.Mailbox;
using Auxilia.Contracts.Mailbox;

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
    }

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
