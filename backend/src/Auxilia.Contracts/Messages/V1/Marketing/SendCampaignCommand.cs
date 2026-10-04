namespace Auxilia.Contracts.Messages.V1.Marketing;

/// <summary>
/// Sends a campaign (N01): snapshot of the recipients, exclusions, then batches through Messaging as the user who asked
/// for it (<paramref name="Roles"/>: their roles, which choose the sending account by the rules, N03).
/// </summary>
public sealed record SendCampaignCommand(Guid CampaignId, IReadOnlyList<string> Roles) : ITenantMessage;
