using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Messaging;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Messaging;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Messaging;

/// <summary>
/// Sending accounts, sender rules, test send and outbound log of a tenant (S-03, N03, D-16): technical endpoints for the
/// System only, with a platform token scoped to the tenant (D-21). Secrets go in, never out.
/// </summary>
internal sealed class MessagingEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var messaging = api.MapGroup("/messaging").WithTags("Messaging").RequirePlatformTenant();

        messaging.MapGet("/accounts", ListAccountsAsync)
            .WithName("ListMessagingAccounts")
            .WithSummary("Sending accounts of the tenant (without secrets)")
            .Produces<IReadOnlyList<MessagingAccountResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        messaging.MapPost("/accounts", CreateAccountAsync)
            .WithName("CreateMessagingAccount")
            .WithSummary("Adds a sending account; the first of a channel becomes its default")
            .Produces<CreateMessagingAccountResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        messaging.MapPut("/accounts/{id:guid}", UpdateAccountAsync)
            .WithName("UpdateMessagingAccount")
            .WithSummary("Changes name and settings; an empty secret keeps the stored one")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        messaging.MapPost("/accounts/{id:guid}/default", SetDefaultAsync)
            .WithName("SetDefaultMessagingAccount")
            .WithSummary("Makes the account the default of its channel")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        messaging.MapPut("/accounts/{id:guid}/active", SetActiveAsync)
            .WithName("SetMessagingAccountActive")
            .WithSummary("Activates or deactivates the account (the default stays active)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        messaging.MapPost("/accounts/{id:guid}/test", SendTestAsync)
            .WithName("SendTestMessage")
            .WithSummary("Sends a test message now and records it in the outbound log with its outcome")
            .Produces<SendTestMessageResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        messaging.MapGet("/rules", ListRulesAsync)
            .WithName("ListSenderRules")
            .WithSummary("Sender rules (channel, purpose, role → account), optionally of one channel")
            .Produces<IReadOnlyList<SenderRuleResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        messaging.MapPut("/rules/{channel}", SetRulesAsync)
            .WithName("SetSenderRules")
            .WithSummary("Replaces the sender rules of a channel")
            .Produces<IReadOnlyList<SenderRuleResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        messaging.MapGet("/outbound-messages", ListOutboundAsync)
            .WithName("ListOutboundMessages")
            .WithSummary("Outbound log, newest first, filtered by channel, status, account and recipient")
            .Produces<PagedResponse<OutboundMessageResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ListAccountsAsync(IMessagingQueryService query, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.ListAccountsAsync(cancellationToken));

    private static async Task<IResult> CreateAccountAsync(CreateMessagingAccountRequest request, IMessagingAccountManager accounts, CancellationToken cancellationToken)
    {
        if (!MessagingInput.TryParseChannel(request.Channel, out var channel))
        {
            return Invalid("channel");
        }

        return (await accounts.CreateAccountAsync(new CreateMessagingAccount(channel, request.Provider, request.Name, request.Settings, request.Secret), cancellationToken))
            .ToHttpResult(id => TypedResults.Created($"/api/v1/messaging/accounts/{id}", new CreateMessagingAccountResponse(id)));
    }

    private static async Task<IResult> UpdateAccountAsync(Guid id, UpdateMessagingAccountRequest request, IMessagingAccountManager accounts, CancellationToken cancellationToken) =>
        (await accounts.UpdateAccountAsync(new UpdateMessagingAccount(id, request.Name, request.Settings, request.Secret), cancellationToken))
            .ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SetDefaultAsync(Guid id, IMessagingAccountManager accounts, CancellationToken cancellationToken) =>
        (await accounts.SetDefaultAccountAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SetActiveAsync(Guid id, SetMessagingAccountActiveRequest request, IMessagingAccountManager accounts, CancellationToken cancellationToken) =>
        (await accounts.SetAccountActiveAsync(id, request.IsActive, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SendTestAsync(Guid id, SendTestMessageRequest request, IMessagingAccountManager accounts, CancellationToken cancellationToken) =>
        (await accounts.SendTestAsync(id, request.Recipient, request.Language, cancellationToken))
            .ToHttpResult(outcome => TypedResults.Ok(new SendTestMessageResponse(outcome.MessageId, outcome.Sent, outcome.ErrorCode)));

    private static async Task<IResult> ListRulesAsync(IMessagingQueryService query, [FromQuery(Name = "filter[channel]")] string? channel, CancellationToken cancellationToken)
    {
        MessageChannel? parsed = null;
        if (channel is not null)
        {
            if (!MessagingInput.TryParseChannel(channel, out var value))
            {
                return Invalid("channel");
            }

            parsed = value;
        }

        return TypedResults.Ok(await query.ListRulesAsync(parsed, cancellationToken));
    }

    private static async Task<IResult> SetRulesAsync(
        string channel, SetSenderRulesRequest request, IMessagingAccountManager accounts, IMessagingQueryService query, CancellationToken cancellationToken)
    {
        if (!MessagingInput.TryParseChannel(channel, out var parsed))
        {
            return Invalid("channel");
        }

        var rules = new List<SenderRuleInput>(request.Rules.Count);
        for (var index = 0; index < request.Rules.Count; index++)
        {
            var rule = request.Rules[index];
            if (!MessagingInput.TryParsePurpose(rule.Purpose, out var purpose))
            {
                return Result.Failure(Errors.Messaging.SenderRuleInvalid(index)).ToHttpResult(TypedResults.NoContent);
            }

            rules.Add(new SenderRuleInput(purpose, string.IsNullOrWhiteSpace(rule.Role) ? null : rule.Role, rule.AccountId, rule.Priority));
        }

        var set = await accounts.SetSenderRulesAsync(parsed, rules, cancellationToken);
        return set.IsFailure
            ? set.ToHttpResult(TypedResults.NoContent)
            : TypedResults.Ok(await query.ListRulesAsync(parsed, cancellationToken));
    }

    private static async Task<IResult> ListOutboundAsync(
        IMessagingQueryService query,
        string? search,
        [FromQuery(Name = "filter[channel]")] string? channel,
        [FromQuery(Name = "filter[status]")] string? status,
        [FromQuery(Name = "filter[accountId]")] Guid? accountId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await query.ListOutboundAsync(new OutboundMessageQuery(channel, status, search, accountId, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static IResult Invalid(string field) => Result.Failure(MessagingInput.InvalidField(field)).ToHttpResult(TypedResults.NoContent);
}
