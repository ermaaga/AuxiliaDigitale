using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Marketing
    {
        public static Error SegmentInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Marketing.SegmentInvalid, "The segment is not valid", errors);

        public static Error SegmentInvalid(string field, string messageKey) =>
            SegmentInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error SegmentNotFound() =>
            Error.NotFound(EventCodes.Marketing.SegmentNotFound, "Segment not found");

        public static Error ListInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Marketing.ListInvalid, "The list is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error ListNotFound() =>
            Error.NotFound(EventCodes.Marketing.ListNotFound, "List not found");

        public static Error TemplateInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Marketing.TemplateInvalid, "The template is not valid", errors);

        public static Error TemplateInvalid(string field, string messageKey) =>
            TemplateInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error TemplateNotFound() =>
            Error.NotFound(EventCodes.Marketing.TemplateNotFound, "Template not found");

        public static Error TemplateInUse() =>
            Error.Conflict(EventCodes.Marketing.TemplateInUse, "The template is used by campaigns");

        public static Error CampaignInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Marketing.CampaignInvalid, "The campaign is not valid", errors);

        public static Error CampaignInvalid(string field, string messageKey) =>
            CampaignInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error CampaignNotFound() =>
            Error.NotFound(EventCodes.Marketing.CampaignNotFound, "Campaign not found");

        public static Error CampaignNotDraft(string status) =>
            Error.Conflict(EventCodes.Marketing.CampaignNotDraft, $"The campaign is {status}: it is sent once, never changed after");

        public static Error CampaignFailed(string reason) =>
            Error.Failure(EventCodes.Marketing.CampaignFailed, $"The campaign cannot be sent: {reason}");

        public static Error SuppressionInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Marketing.SuppressionInvalid, "The suppression is not valid", errors);

        public static Error SuppressionNotFound() =>
            Error.NotFound(EventCodes.Marketing.SuppressionNotFound, "Suppression not found");
    }
}
