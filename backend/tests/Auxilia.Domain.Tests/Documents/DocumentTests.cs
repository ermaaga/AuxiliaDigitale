using Auxilia.Diagnostics;
using Auxilia.Domain.Documents;

namespace Auxilia.Domain.Tests.Documents;

public sealed class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static DocumentUpload Upload(string fileName = "scan.pdf", int year = 2026, Guid? caseId = null, Guid? folderId = null) =>
        new(Guid.CreateVersion7(), caseId, folderId, fileName, "tenants/demo/documents/2026/10/x.pdf", "application/pdf", 10, new string('a', 64), year, null, "  note  ", "{}");

    [Fact]
    public void Upload_StartsProcessing_AndTrimsTheDescription()
    {
        var document = Document.Upload(Guid.CreateVersion7(), Upload(), Guid.CreateVersion7(), Now).Value;

        (document.Status, document.Description, document.Extension, document.UploadedAt).ShouldBe((DocumentStatus.Processing, "note", ".pdf", Now));
    }

    [Theory]
    [InlineData(2016, true)]
    [InlineData(2015, false)]
    [InlineData(2027, true)]
    [InlineData(2028, false)]
    public void ReferenceYear_AtMostTenYearsBack(int year, bool valid) =>
        Document.Upload(Guid.CreateVersion7(), Upload(year: year), null, Now).IsSuccess.ShouldBe(valid);

    [Fact]
    public void Upload_InvalidValues_AreReportedTogether()
    {
        var result = Document.Upload(Guid.CreateVersion7(), Upload("noextension", 2000, folderId: Guid.CreateVersion7()), null, Now);

        result.Error!.Code.ShouldBe(EventCodes.Documents.DocumentInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["fileName", "referenceYear", "folderId"], ignoreOrder: true);
    }

    [Fact]
    public void Update_KeepsTheExtension()
    {
        var document = Document.Upload(Guid.CreateVersion7(), Upload(), null, Now).Value;
        var area = Guid.CreateVersion7();

        document.Update("renamed.pdf", 2025, area, null, "{\"x\":1}", Today).IsSuccess.ShouldBeTrue();
        (document.FileName, document.ReferenceYear, document.AreaId, document.Description, document.CustomFields)
            .ShouldBe(("renamed.pdf", 2025, (Guid?)area, (string?)null, "{\"x\":1}"));
        document.Update("renamed.docx", 2025, null, null, "{}", Today).Error!.ValidationErrors.Keys.ShouldBe(["fileName"]);
        document.Update("x.pdf", 2025, null, new string('x', Document.DescriptionMaxLength + 1), "{}", Today).Error!.ValidationErrors.Keys.ShouldBe(["description"]);
    }

    [Fact]
    public void MoveTo_OnlyCaseDocumentsHaveFolders_AndProcessingHappensOnce()
    {
        var loose = Document.Upload(Guid.CreateVersion7(), Upload(), null, Now).Value;
        var ofCase = Document.Upload(Guid.CreateVersion7(), Upload(caseId: Guid.CreateVersion7()), null, Now).Value;
        var folder = Guid.CreateVersion7();

        loose.MoveTo(folder).IsFailure.ShouldBeTrue();
        ofCase.MoveTo(folder).IsSuccess.ShouldBeTrue();
        ofCase.FolderId.ShouldBe(folder);
        ofCase.MoveTo(null).IsSuccess.ShouldBeTrue();

        ofCase.Processed(intact: false).ShouldBeTrue();
        ofCase.Processed(intact: true).ShouldBeFalse();
        ofCase.Status.ShouldBe(DocumentStatus.Damaged);
    }

    [Fact]
    public void Area_NameRequired_AndDeactivation()
    {
        var area = DocumentArea.Create(Guid.CreateVersion7(), " Fiscale ").Value;
        area.Name.ShouldBe("Fiscale");

        area.Update("Fisco", isActive: false).IsSuccess.ShouldBeTrue();
        (area.Name, area.IsActive).ShouldBe(("Fisco", false));
        area.Update(" ", true).Error!.Code.ShouldBe(EventCodes.Documents.DocumentAreaInvalid);
        DocumentArea.Create(Guid.CreateVersion7(), new string('x', DocumentArea.NameMaxLength + 1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ImportLegacy_KeepsTheUploadInstantAndAnOldYear_AndIsAvailable()
    {
        var uploaded = Now.AddYears(-12);
        var uploader = Guid.CreateVersion7();

        var document = Document.ImportLegacy(Guid.CreateVersion7(), Upload(year: 2012), uploader, uploaded).Value;

        (document.UploadedAt, document.UploadedByUserId, document.ReferenceYear, document.Status).ShouldBe((uploaded, (Guid?)uploader, 2012, DocumentStatus.Available));
    }

    [Theory]
    [InlineData("no-extension", false, "fileName")]
    [InlineData("scan.pdf", true, "folderId")]
    public void ImportLegacy_FileNameAndFolderRules_Apply(string fileName, bool folderWithoutCase, string field)
    {
        var result = Document.ImportLegacy(Guid.CreateVersion7(), Upload(fileName, folderId: folderWithoutCase ? Guid.CreateVersion7() : null), null, Now);

        result.Error!.ValidationErrors!.Keys.ShouldBe([field]);
    }
}
