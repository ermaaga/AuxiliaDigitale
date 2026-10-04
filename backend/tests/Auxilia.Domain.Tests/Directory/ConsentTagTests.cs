using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;

namespace Auxilia.Domain.Tests.Directory;

public sealed class ConsentTagTests
{
    [Fact]
    public void Tags_TrimTheName_AndAcceptOnlyHexColours()
    {
        var tag = Tag.Create(Guid.CreateVersion7(), " CAF ", "#72fa29").Value;
        (tag.Name, tag.Color).ShouldBe(("CAF", "#72FA29"));
        tag.Update("VIP", " ").IsSuccess.ShouldBeTrue();
        tag.Color.ShouldBeNull();

        var invalid = Tag.Create(Guid.CreateVersion7(), new string('x', 51), "blue");
        invalid.Error!.Code.ShouldBe(EventCodes.Directory.TagInvalid);
        invalid.Error.ValidationErrors.Keys.ShouldBe(["name", "color"], ignoreOrder: true);
    }

    [Fact]
    public void Consents_RecordWhoWhenAndWhy()
    {
        var person = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
        var consent = Consent.Record(Guid.CreateVersion7(), person, ConsentPurpose.Marketing, ConsentChannel.Email, true, ConsentSource.LegacyMigration, " v1 ", " ", user, now).Value;

        (consent.PersonId, consent.Granted, consent.Source, consent.Version, consent.Note, consent.RecordedByUserId, consent.RecordedAt)
            .ShouldBe((person, true, ConsentSource.LegacyMigration, "v1", (string?)null, (Guid?)user, now));

        var invalid = Consent.Record(Guid.CreateVersion7(), person, (ConsentPurpose)9, (ConsentChannel)9, false, ConsentSource.Staff, new string('v', 51), new string('n', 501), null, now);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["purpose", "channel", "version", "note"], ignoreOrder: true);

        var assignment = new PersonTag(person, Guid.CreateVersion7(), user, now);
        (assignment.PersonId, assignment.AssignedByUserId, assignment.AssignedAt).ShouldBe((person, (Guid?)user, now));
    }
}
