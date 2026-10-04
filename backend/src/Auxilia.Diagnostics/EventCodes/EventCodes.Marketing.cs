namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(19000, "Marketing")]
    public static class Marketing
    {
        /// <summary>A dynamic segment was created (N01, M-02).</summary>
        public const int SegmentCreated = 19001;

        /// <summary>A segment was renamed or its rule changed.</summary>
        public const int SegmentUpdated = 19002;

        /// <summary>A segment was deleted.</summary>
        public const int SegmentDeleted = 19003;

        /// <summary>A segment or its rule is not valid (400, field errors <c>rule.conditions[i].value</c>…).</summary>
        public const int SegmentInvalid = 19004;

        /// <summary>No segment with this id (404).</summary>
        public const int SegmentNotFound = 19005;

        /// <summary>A static list was created.</summary>
        public const int ListCreated = 19006;

        /// <summary>A static list was renamed.</summary>
        public const int ListUpdated = 19007;

        /// <summary>A static list was deleted with its members.</summary>
        public const int ListDeleted = 19008;

        /// <summary>A static list or its members are not valid (400, field errors).</summary>
        public const int ListInvalid = 19009;

        /// <summary>No static list with this id (404).</summary>
        public const int ListNotFound = 19010;

        /// <summary>Clients were added to or removed from a static list.</summary>
        public const int ListMembersChanged = 19011;

        /// <summary>An e-mail template of the campaigns was created (M-03).</summary>
        public const int TemplateCreated = 19012;

        /// <summary>An e-mail template was changed.</summary>
        public const int TemplateUpdated = 19013;

        /// <summary>An e-mail template without campaigns was deleted.</summary>
        public const int TemplateDeleted = 19014;

        /// <summary>A template is not valid: name, language, subject or body (400, field errors).</summary>
        public const int TemplateInvalid = 19015;

        /// <summary>No e-mail template with this id (404).</summary>
        public const int TemplateNotFound = 19016;

        /// <summary>A template used by campaigns cannot be deleted (409).</summary>
        public const int TemplateInUse = 19017;

        /// <summary>A test of a template was queued to an address.</summary>
        public const int TemplateTestSent = 19018;

        /// <summary>A campaign was created.</summary>
        public const int CampaignCreated = 19019;

        /// <summary>A draft campaign was changed.</summary>
        public const int CampaignUpdated = 19020;

        /// <summary>A draft or finished campaign was deleted.</summary>
        public const int CampaignDeleted = 19021;

        /// <summary>A campaign is not valid: name, template or audience (400, field errors).</summary>
        public const int CampaignInvalid = 19022;

        /// <summary>No campaign with this id (404).</summary>
        public const int CampaignNotFound = 19023;

        /// <summary>The campaign is not a draft (sent once, never twice) (409).</summary>
        public const int CampaignNotDraft = 19024;

        /// <summary>"Send now": the campaign was queued for the Worker.</summary>
        public const int CampaignQueued = 19025;

        /// <summary>The Worker sent a campaign: recipients, sent, failed, excluded.</summary>
        public const int CampaignSent = 19026;

        /// <summary>A campaign could not be sent (template, audience or account missing).</summary>
        public const int CampaignFailed = 19027;

        /// <summary>A campaign was cancelled (draft, or still sending).</summary>
        public const int CampaignCancelled = 19028;

        /// <summary>An address was added to the marketing suppressions.</summary>
        public const int SuppressionAdded = 19029;

        /// <summary>An address was removed from the suppressions.</summary>
        public const int SuppressionRemoved = 19030;

        /// <summary>A suppression is not valid or the address is already there (400, field errors).</summary>
        public const int SuppressionInvalid = 19031;

        /// <summary>No suppression with this id (404).</summary>
        public const int SuppressionNotFound = 19032;

        /// <summary>A batch of recipients of a campaign was processed.</summary>
        public const int CampaignBatchSent = 19033;
    }
}
