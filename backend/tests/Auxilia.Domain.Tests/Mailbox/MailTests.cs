using Auxilia.Domain.Mailbox;

namespace Auxilia.Domain.Tests.Mailbox;

public sealed class MailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly Guid Folder = Guid.CreateVersion7();

    private static MailMessageData Message(string id = "m1", string? subject = "Pratica 2026/15", bool read = false, long size = 2048) =>
        new(id, "<abc@example.com>", "t1", "Mario.Rossi@Example.com", "Mario Rossi", ["Studio <studio@example.com>"], [], subject,
            Now.AddMinutes(-5), Now, read, size, false, ["fattura.pdf"]);

    [Fact]
    public void MailFolder_KeepsKindAndCursorKey_AndFollowsARename()
    {
        var folder = MailFolder.Create(Guid.CreateVersion7(), Account, new MailFolderData("INBOX", "Posta in arrivo", MailFolderKind.Inbox), Now);
        (folder.Kind, folder.Name, folder.CursorKey).ShouldBe((MailFolderKind.Inbox, "Posta in arrivo", "mail:inbox"));

        folder.Apply(new MailFolderData("INBOX", "  ", MailFolderKind.Inbox), Now.AddDays(1));
        (folder.Name, folder.UpdatedAt).ShouldBe(("Inbox", Now.AddDays(1)));
        MailFolder.KeyOf(MailFolderKind.Sent).ShouldBe("mail:sent");

        Should.Throw<ArgumentException>(() => folder.Apply(new MailFolderData("SENT", "Inviata", MailFolderKind.Sent), Now));
        MailFolder.IsUsable(new MailFolderData("", "x", MailFolderKind.Inbox)).ShouldBeFalse();
        MailFolder.IsUsable(new MailFolderData("x", "x", (MailFolderKind)9)).ShouldBeFalse();
    }

    [Fact]
    public void MailMessage_StoresBoundedCleanHeaders_WithoutBodies()
    {
        var data = Message() with
        {
            Subject = "Oggetto\r\n\tsu due righe" + new string('x', 600),
            To = [.. Enumerable.Range(0, 30).Select(index => $"r{index}@example.com")],
            AttachmentNames = ["fattura.pdf", " ", "contratto\u0000.pdf"],
        };

        var message = MailMessage.Create(Guid.CreateVersion7(), Account, Folder, data, Now);

        message.Subject.Length.ShouldBe(MailMessage.SubjectMaxLength);
        message.Subject.ShouldStartWith("Oggetto   su due righe");
        message.FromAddress.ShouldBe("mario.rossi@example.com");
        message.To.Count.ShouldBe(MailMessage.MaxRecipients);
        message.AttachmentNames.ShouldBe(["fattura.pdf", "contratto .pdf"]);
        (message.HasAttachments, message.IsRead, message.SyncedAt).ShouldBe((true, false, Now));
    }

    [Fact]
    public void MailMessage_Apply_TellsWhetherSomethingShownChanged()
    {
        var message = MailMessage.Create(Guid.CreateVersion7(), Account, Folder, Message(), Now);

        message.Apply(Message(), Now.AddMinutes(1)).ShouldBeFalse();
        message.SyncedAt.ShouldBe(Now.AddMinutes(1));
        message.Apply(Message(read: true), Now.AddMinutes(2)).ShouldBeTrue();
        message.IsRead.ShouldBeTrue();
    }

    [Fact]
    public void MailMessage_RefusesMalformedHeaders()
    {
        MailMessage.IsUsable(Message(id: " ")).ShouldBeFalse();
        MailMessage.IsUsable(Message(id: new string('a', 501))).ShouldBeFalse();
        MailMessage.IsUsable(Message(size: -1)).ShouldBeFalse();
        MailMessage.IsUsable(Message(size: MailMessage.MaxSize + 1)).ShouldBeFalse();
        MailMessage.IsUsable(Message(subject: null)).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => MailMessage.Create(Guid.CreateVersion7(), Account, Folder, Message(id: ""), Now));
        MailMessage.Create(Guid.CreateVersion7(), Account, Folder, Message(subject: null), Now).Subject.ShouldBe(string.Empty);
    }

    [Fact]
    public void SyncRun_CountsArrivedMail()
    {
        var run = SyncRun.Queue(Guid.CreateVersion7(), Account, Guid.CreateVersion7(), SyncScope.Mail, SyncTrigger.PageOpen, Now);
        run.RecordArrived(2);
        run.RecordArrived(0);
        run.ArrivedCount.ShouldBe(2);
        Should.Throw<ArgumentOutOfRangeException>(() => run.RecordArrived(-1));
    }

    [Fact]
    public void FiledMessage_KeepsOnlyHashes()
    {
        var hash = new string('a', 64);
        var filed = FiledMessage.Record(Guid.CreateVersion7(), hash, new string('0', 64), Folder, Account, Guid.CreateVersion7(), Now);

        (filed.MessageKeyHash, filed.CaseId, filed.DocumentId, filed.FiledAt).ShouldBe((hash, Folder, Account, Now));
        Should.Throw<ArgumentException>(() => FiledMessage.Record(Guid.CreateVersion7(), "mario@example.com", hash, Folder, Account, Guid.CreateVersion7(), Now));
        Should.Throw<ArgumentException>(() => FiledMessage.Record(Guid.CreateVersion7(), hash, new string('A', 64), Folder, Account, Guid.CreateVersion7(), Now));
    }
}
