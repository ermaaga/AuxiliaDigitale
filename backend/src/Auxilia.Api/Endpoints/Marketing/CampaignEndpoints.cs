using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Marketing;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Marketing;

namespace Auxilia.Api.Endpoints.Marketing;

/// <summary>
/// E-mail templates, campaigns and suppressions (N01, M-03): "send now" queues a draft once; the Worker sends it in
/// batches. Module <c>marketing</c>: 404 when not visible to the role.
/// </summary>
internal sealed class CampaignEndpoints : IModuleEndpoints
{
    public string ModuleCode => MarketingModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var templates = module.MapGroup("/marketing/templates").WithTags("Marketing");

        templates.MapGet("/", async (IEmailTemplateQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.ListAsync(cancellationToken)))
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("ListEmailTemplates")
            .WithSummary("The e-mail templates of the campaigns, by name")
            .Produces<IReadOnlyList<EmailTemplateListItemResponse>>();

        templates.MapPost("/", CreateTemplateAsync)
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("CreateEmailTemplate")
            .WithSummary("Creates an e-mail template (Liquid subject and body)")
            .Produces<CreatedAudienceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        templates.MapGet("/{id:guid}", GetTemplateAsync)
            .WithETag()
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("GetEmailTemplate")
            .WithSummary("An e-mail template")
            .Produces<EmailTemplateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        templates.MapPut("/{id:guid}", UpdateTemplateAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("UpdateEmailTemplate")
            .WithSummary("Changes an e-mail template")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        templates.MapDelete("/{id:guid}", DeleteTemplateAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("DeleteEmailTemplate")
            .WithSummary("Deletes an e-mail template no campaign uses")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        templates.MapPost("/{id:guid}/preview", PreviewTemplateAsync)
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("PreviewEmailTemplate")
            .WithSummary("The template rendered for a client, or a sample person")
            .Produces<EmailPreviewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        templates.MapPost("/{id:guid}/test", TestTemplateAsync)
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("TestEmailTemplate")
            .WithSummary("Sends the template rendered for a sample person to an address (purpose Marketing)")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var campaigns = module.MapGroup("/marketing/campaigns").WithTags("Marketing");

        campaigns.MapGet("/", async (ICampaignQueryService query, int? page, int? pageSize, CancellationToken cancellationToken) =>
                TypedResults.Ok(await query.ListAsync(page ?? 1, pageSize ?? 25, cancellationToken)))
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("ListCampaigns")
            .WithSummary("The campaigns, newest first, with their results")
            .Produces<PagedResponse<CampaignResponse>>();

        campaigns.MapPost("/", CreateCampaignAsync)
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("CreateCampaign")
            .WithSummary("Creates a draft campaign with a template and a segment or a static list")
            .Produces<CreatedAudienceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        campaigns.MapGet("/{id:guid}", GetCampaignAsync)
            .WithETag()
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("GetCampaign")
            .WithSummary("A campaign with its status and counters")
            .Produces<CampaignResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        campaigns.MapPut("/{id:guid}", UpdateCampaignAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("UpdateCampaign")
            .WithSummary("Changes a draft campaign")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        campaigns.MapDelete("/{id:guid}", DeleteCampaignAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("DeleteCampaign")
            .WithSummary("Deletes a campaign not sending")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        campaigns.MapGet("/{id:guid}/audience", AudienceAsync)
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("GetCampaignAudience")
            .WithSummary("How many clients the audience has now (confirmation of send now)")
            .Produces<CampaignAudienceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        campaigns.MapPost("/{id:guid}/send", SendAsync)
            .RequirePermission(MarketingPermissions.SendCampaigns)
            .WithName("SendCampaign")
            .WithSummary("Send now: the Worker snapshots the recipients and sends in batches; once only")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        campaigns.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequirePermission(MarketingPermissions.SendCampaigns)
            .WithName("CancelCampaign")
            .WithSummary("Cancels a draft or a campaign still sending")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        campaigns.MapGet("/{id:guid}/recipients", RecipientsAsync)
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("ListCampaignRecipients")
            .WithSummary("The recipients of a campaign with their status and exclusion reason")
            .Produces<PagedResponse<CampaignRecipientResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var suppressions = module.MapGroup("/marketing/suppressions").WithTags("Marketing");

        suppressions.MapGet("/", async (ISuppressionQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.ListAsync(cancellationToken)))
            .RequirePermission(MarketingPermissions.ViewCampaigns)
            .WithName("ListSuppressions")
            .WithSummary("Addresses marketing never writes to")
            .Produces<IReadOnlyList<SuppressionResponse>>();

        suppressions.MapPost("/", AddSuppressionAsync)
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("AddSuppression")
            .WithSummary("Adds an address marketing never writes to")
            .Produces<CreatedAudienceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        suppressions.MapDelete("/{id:guid}", RemoveSuppressionAsync)
            .RequirePermission(MarketingPermissions.ManageCampaigns)
            .WithName("RemoveSuppression")
            .WithSummary("Removes a suppression")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateTemplateAsync(SaveEmailTemplateRequest request, IEmailTemplateManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken)).ToHttpResult(id => TypedResults.Created($"/api/v1/marketing/templates/{id}", new CreatedAudienceResponse(id)));

    private static async Task<IResult> GetTemplateAsync(Guid id, IEmailTemplateQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UpdateTemplateAsync(Guid id, SaveEmailTemplateRequest request, IEmailTemplateManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteTemplateAsync(Guid id, IEmailTemplateManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> PreviewTemplateAsync(Guid id, PreviewEmailTemplateRequest request, IEmailTemplateQueryService query, CancellationToken cancellationToken) =>
        (await query.PreviewAsync(id, request.ClientId, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> TestTemplateAsync(Guid id, TestEmailTemplateRequest request, IEmailTemplateManager manager, CancellationToken cancellationToken) =>
        (await manager.SendTestAsync(id, request.Email, cancellationToken)).ToHttpResult(() => TypedResults.Accepted((string?)null));

    private static async Task<IResult> CreateCampaignAsync(SaveCampaignRequest request, ICampaignManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken)).ToHttpResult(id => TypedResults.Created($"/api/v1/marketing/campaigns/{id}", new CreatedAudienceResponse(id)));

    private static async Task<IResult> GetCampaignAsync(Guid id, ICampaignQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UpdateCampaignAsync(Guid id, SaveCampaignRequest request, ICampaignManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteCampaignAsync(Guid id, ICampaignManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> AudienceAsync(Guid id, ICampaignQueryService query, CancellationToken cancellationToken) =>
        (await query.AudienceAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SendAsync(Guid id, ICampaignManager manager, CancellationToken cancellationToken) =>
        (await manager.SendAsync(id, cancellationToken)).ToHttpResult(() => TypedResults.Accepted($"/api/v1/marketing/campaigns/{id}"));

    private static async Task<IResult> CancelAsync(Guid id, ICampaignManager manager, CancellationToken cancellationToken) =>
        (await manager.CancelAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> RecipientsAsync(Guid id, ICampaignQueryService query, string? status, int? page, int? pageSize, CancellationToken cancellationToken) =>
        (await query.RecipientsAsync(id, status, page ?? 1, pageSize ?? 25, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> AddSuppressionAsync(AddSuppressionRequest request, ISuppressionManager manager, CancellationToken cancellationToken) =>
        (await manager.AddAsync(request, cancellationToken)).ToHttpResult(id => TypedResults.Created($"/api/v1/marketing/suppressions/{id}", new CreatedAudienceResponse(id)));

    private static async Task<IResult> RemoveSuppressionAsync(Guid id, ISuppressionManager manager, CancellationToken cancellationToken) =>
        (await manager.RemoveAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);
}
