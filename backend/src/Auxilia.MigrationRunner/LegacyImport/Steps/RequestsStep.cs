using Auxilia.Domain.Engagement;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F15): <c>Requests</c> → <c>engagement.requests</c>: <c>Message</c> is message #1 (the sender), a non-empty
/// <c>Response</c> message #2 (the recipient, or an Administrator for the office).
/// </summary>
internal sealed class RequestsStep : ILegacyImportStep
{
    public const string Table = "Requests";

    public string Name => "requests";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var users = await MigratedUsers.LoadAsync(context, cancellationToken);
        var legacyUsers = await context.Legacy.Users.Select(user => user.Id).ToListAsync(cancellationToken);
        var office = users.AnyAdministrator(context, legacyUsers);
        var existing = await context.Tenant.Set<Request>().ToDictionaryAsync(request => request.Id, cancellationToken);
        var result = context.Report.For(Table);
        foreach (var legacy in await context.Legacy.Requests.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (users.User(context, legacy.SenderId) is not { } senderId)
            {
                context.Report.Skip(Table, legacy.Id, "sender not migrated");
                continue;
            }

            Guid? recipientId = null;
            if (legacy.ReceiverId is { } receiver && (recipientId = users.User(context, receiver)) is null)
            {
                context.Report.Warn(Table, legacy.Id, "recipient not migrated: sent to the office");
            }

            var responder = recipientId ?? office;
            var response = string.IsNullOrWhiteSpace(legacy.Response) ? null : LegacyText.Fit(legacy.Response, Request.BodyMaxLength, out _);
            var respondedAt = LegacyImportContext.Instant(legacy.RespondedAt ?? legacy.CreatedAt);

            if (context.Ids.Find(Table, legacy.Id) is { } id && existing.TryGetValue(id, out var current))
            {
                if (response is not null && responder is { } author && current.ImportLegacyReply(author, response, respondedAt))
                {
                    result.Updated++;
                }

                continue;
            }

            if (!Enum.TryParse<RequestType>(legacy.Type, ignoreCase: false, out var type) || !Enum.IsDefined(type))
            {
                context.Report.Warn(Table, legacy.Id, "unknown type: General");
                type = RequestType.General;
            }

            var subject = LegacyText.Fit(legacy.Subject, Request.SubjectMaxLength, out var subjectCut);
            var body = LegacyText.Fit(legacy.Message, Request.BodyMaxLength, out var bodyCut);
            if (subjectCut || bodyCut)
            {
                context.Report.Warn(Table, legacy.Id, "subject or text too long: cut");
            }

            var imported = Request.ImportLegacy(
                context.Ids.NewId(), senderId, recipientId, type, subject.Length == 0 ? "-" : subject, body.Length == 0 ? "-" : body,
                LegacyImportContext.Instant(legacy.CreatedAt));
            if (imported.IsFailure)
            {
                context.Report.Skip(Table, legacy.Id, "the request is not valid");
                continue;
            }

            if (response is not null)
            {
                if (responder is { } author)
                {
                    imported.Value.ImportLegacyReply(author, response, respondedAt);
                }
                else
                {
                    context.Report.Warn(Table, legacy.Id, "response without a migrated author: left out");
                }
            }

            context.Tenant.Add(imported.Value);
            context.Ids.Add(Table, legacy.Id, imported.Value.Id);
            result.Created++;
        }
    }
}
