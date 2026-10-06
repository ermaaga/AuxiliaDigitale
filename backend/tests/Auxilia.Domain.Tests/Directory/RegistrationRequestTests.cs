using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Platform;

namespace Auxilia.Domain.Tests.Directory;

public sealed class RegistrationRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static RegistrationDetails Details(
        string? email = " Mario.Rossi@Example.TEST ", string? phone = "+39 333 123 4567", DateOnly? birthDate = null, string? fiscalCode = "rssmra80a01h501u",
        bool consent = true, string? privacyVersion = " 2026-01 ") =>
        new(" Mario ", "Rossi", email, phone, birthDate ?? new DateOnly(1980, 1, 1), fiscalCode, consent, privacyVersion);

    private static RegistrationRequest Submit(RegistrationDetails? details = null, int minimumAge = 16) =>
        RegistrationRequest.Submit(Guid.CreateVersion7(), details ?? Details(), "it", "site", minimumAge, Now).Value;

    [Fact]
    public void Submit_Normalises_AndStoresTheConsent()
    {
        var request = Submit();

        (request.FirstName, request.Email, request.FiscalCode, request.PrivacyVersion).ShouldBe(("Mario", "mario.rossi@example.test", "RSSMRA80A01H501U", "2026-01"));
        (request.Status, request.RequestedAt, request.PrivacyConsentedAt, request.ClientApplication).ShouldBe((RegistrationStatus.Pending, Now, Now, "site"));
        request.ToPersonDetails().ShouldBe(new PersonDetails("Mario", "Rossi", "mario.rossi@example.test", new DateOnly(1980, 1, 1), "+39 333 123 4567", "RSSMRA80A01H501U"));
    }

    [Fact]
    public void Submit_EveryFieldIsRequired_AndTheConsentToo()
    {
        var result = RegistrationRequest.Submit(
            Guid.CreateVersion7(), new RegistrationDetails("", "", "", "", null, "", false, null), "it", "site", 16, Now);

        result.Error!.Code.ShouldBe(EventCodes.Directory.RegistrationInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(
            ["firstName", "lastName", "email", "phone", "birthDate", "fiscalCode", "privacyConsent", "privacyVersion"], ignoreOrder: true);
    }

    [Fact]
    public void Submit_FollowsThePersonRules()
    {
        var result = RegistrationRequest.Submit(
            Guid.CreateVersion7(), Details(email: "nope", phone: "12", fiscalCode: "ABC"), "it", "site", 16, Now);

        result.Error!.ValidationErrors.Keys.ShouldBe(["email", "phone", "fiscalCode"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(2010, 10, 3, true)]
    [InlineData(2010, 10, 4, false)]
    public void Submit_ApplicantMustHaveTheMinimumAge(int year, int month, int day, bool accepted)
    {
        var result = RegistrationRequest.Submit(Guid.CreateVersion7(), Details(birthDate: new DateOnly(year, month, day)), "it", "site", 16, Now);

        result.IsSuccess.ShouldBe(accepted);
        if (!accepted)
        {
            result.Error!.ValidationErrors["birthDate"].ShouldBe(["validation.registration.age"]);
        }
    }

    [Fact]
    public void ApproveOrReject_OnlyOnce()
    {
        var processor = Guid.CreateVersion7();
        var client = Guid.CreateVersion7();
        var approved = Submit();
        var rejected = Submit();

        approved.Approve(client, processor, " welcome ", Now).IsSuccess.ShouldBeTrue();
        rejected.Reject(processor, null, Now).IsSuccess.ShouldBeTrue();

        (approved.Status, approved.ClientId, approved.ProcessedByUserId, approved.ProcessedAt, approved.Notes)
            .ShouldBe((RegistrationStatus.Approved, client, processor, Now, "welcome"));
        (rejected.Status, rejected.ClientId, rejected.Notes).ShouldBe((RegistrationStatus.Rejected, (Guid?)null, (string?)null));
        approved.Reject(processor, null, Now).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationProcessed);
        rejected.Approve(client, processor, null, Now).Error!.Code.ShouldBe(EventCodes.Directory.RegistrationProcessed);
    }

    [Fact]
    public void Process_NotesTooLong_AreRefused_AndTheRequestStaysPending()
    {
        var request = Submit();

        request.Reject(Guid.CreateVersion7(), new string('x', RegistrationRequest.NotesMaxLength + 1), Now).Error!.ValidationErrors.Keys.ShouldBe(["notes"]);
        request.Status.ShouldBe(RegistrationStatus.Pending);
    }

    [Theory]
    [InlineData(ClientApplicationType.Mobile, "none", "altcha")]
    [InlineData(ClientApplicationType.Integration, "altcha", "altcha")]
    [InlineData(ClientApplicationType.WebBff, "none", "none")]
    [InlineData(ClientApplicationType.WebBff, "altcha", "altcha")]
    public void ClientApplication_PublicClientsNeverGoWithoutACaptcha(ClientApplicationType type, string stored, string effective)
    {
        var client = ClientApplication.Create(Guid.CreateVersion7(), "client", "Client", type).Value;
        client.SetCaptchaProvider(stored);

        client.EffectiveCaptchaProvider.ShouldBe(effective);
    }

    [Fact]
    public void ImportLegacy_KeepsTheLegacyValuesAndOutcome()
    {
        var processor = Guid.CreateVersion7();
        var client = Guid.CreateVersion7();
        var details = new PersonDetails(" Mario ", "Verdi", " Mario@Example.TEST ", new DateOnly(1980, 5, 10), "3331234567", "vrdmra80e10h501z");

        var imported = RegistrationRequest.ImportLegacy(Guid.CreateVersion7(), details, "it", Now.AddYears(-1), RegistrationStatus.Approved, processor, Now.AddMonths(-11), new string('n', 600), client);

        (imported.FirstName, imported.Email, imported.FiscalCode, imported.ClientApplication, imported.PrivacyVersion).ShouldBe(("Mario", "mario@example.test", "VRDMRA80E10H501Z", "legacy", "legacy"));
        (imported.Status, imported.ProcessedByUserId, imported.ClientId, imported.RequestedAt).ShouldBe((RegistrationStatus.Approved, (Guid?)processor, (Guid?)client, Now.AddYears(-1)));
        imported.Notes!.Length.ShouldBe(RegistrationRequest.NotesMaxLength);
    }
}
