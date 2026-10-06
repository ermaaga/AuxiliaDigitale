using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;

namespace Auxilia.Domain.Tests.Engagement;

public sealed class RequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Sender = Guid.CreateVersion7();
    private static readonly Guid Employee = Guid.CreateVersion7();

    private static Request Open(Guid? recipient = null) =>
        Request.Open(Guid.CreateVersion7(), Sender, recipient, RequestType.Support, " Invoice ", " Where is my invoice? ", Now).Value;

    [Fact]
    public void Open_IsPending_WithTheOriginalTextAsFirstMessage()
    {
        var request = Open(Employee);

        (request.Status, request.Subject, request.RecipientUserId, request.SentAt, request.LastMessageAt).ShouldBe(
            (RequestStatus.Pending, "Invoice", (Guid?)Employee, Now, Now));
        var first = request.Messages.ShouldHaveSingleItem();
        (first.Sequence, first.AuthorUserId, first.Body).ShouldBe((1, Sender, "Where is my invoice?"));
    }

    [Fact]
    public void Open_ChecksTypeSubjectAndMessage()
    {
        var result = Request.Open(Guid.CreateVersion7(), Sender, null, (RequestType)42, " ", new string('x', 4001), Now);

        result.Error!.Code.ShouldBe(EventCodes.Requests.RequestInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["type", "subject", "message"], ignoreOrder: true);
        Request.Open(Guid.CreateVersion7(), Sender, null, RequestType.General, new string('s', 201), "ok", Now)
            .Error!.ValidationErrors.Keys.ShouldBe(["subject"]);
    }

    [Fact]
    public void Replies_AppendMessages_TheOtherPartyResponds_TheSenderFollowsUp()
    {
        var request = Open(Employee);

        request.Reply(Employee, " Sent yesterday ", Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        (request.Status, request.LastMessageAt).ShouldBe((RequestStatus.Responded, Now.AddHours(1)));

        request.Reply(Sender, "Not received", Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(RequestStatus.Pending);
        request.Messages.Select(message => (message.Sequence, message.Body)).ShouldBe([(1, "Where is my invoice?"), (2, "Sent yesterday"), (3, "Not received")]);

        request.Reply(Employee, " ", Now).Error!.ValidationErrors.Keys.ShouldBe(["message"]);
    }

    [Fact]
    public void Close_IsTerminal()
    {
        var request = Open();

        request.Close(Employee, Now).IsSuccess.ShouldBeTrue();
        (request.Status, request.ClosedAt, request.ClosedByUserId).ShouldBe((RequestStatus.Closed, (DateTimeOffset?)Now, (Guid?)Employee));
        request.Close(Employee, Now).Error!.Code.ShouldBe(EventCodes.Requests.RequestIsClosed);
        request.Reply(Sender, "again", Now).Error!.Code.ShouldBe(EventCodes.Requests.RequestIsClosed);
    }

    [Fact]
    public void ImportLegacy_KeepsTheSendInstant_AndTheLegacyReplyOnce()
    {
        var imported = Request.ImportLegacy(Guid.CreateVersion7(), Sender, null, RequestType.Support, "Oggetto", "Testo", Now.AddYears(-1)).Value;

        imported.ImportLegacyReply(Employee, "Risposta", Now.AddYears(-1).AddDays(1)).ShouldBeTrue();
        imported.ImportLegacyReply(Employee, "Di nuovo", Now).ShouldBeFalse();

        (imported.SentAt, imported.Status, imported.LastMessageAt).ShouldBe((Now.AddYears(-1), RequestStatus.Responded, Now.AddYears(-1).AddDays(1)));
        imported.Messages.Select(message => (message.Sequence, message.AuthorUserId, message.Body)).ShouldBe([(1, Sender, "Testo"), (2, Employee, "Risposta")]);
    }
}
