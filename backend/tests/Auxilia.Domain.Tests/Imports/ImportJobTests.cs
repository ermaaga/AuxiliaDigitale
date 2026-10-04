using Auxilia.Domain.Imports;

namespace Auxilia.Domain.Tests.Imports;

public sealed class ImportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

    private static ImportJob Job() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), " March ", new string('f', 300) + ".xlsx", [1, 2], Now);

    [Fact]
    public void Validation_ThenConfirmation_ThenProcessing_CountTheRows()
    {
        var job = Job();
        (job.Name, job.FileName.Length, job.Status, job.IsFinished).ShouldBe(("March", ImportJob.FileNameMaxLength, ImportJobStatus.Pending, false));

        job.StartValidation(3, Now).ShouldBeTrue();
        job.StartValidation(3, Now).ShouldBeTrue();
        job.RecordValidated(2, 1);
        job.AwaitConfirmation();
        (job.Status, job.SuccessRows, job.FailedRows, job.ProcessedRows, job.FileContent).ShouldBe((ImportJobStatus.AwaitingConfirmation, 2, 1, 3, (byte[]?)null));
        job.StartValidation(3, Now).ShouldBeFalse();
        job.Cancel(Now).ShouldBeTrue();
        job.Confirm().ShouldBeFalse();
    }

    [Fact]
    public void Processing_StartsFromTheInvalidRows_AndCompletes()
    {
        var job = Job();
        job.StartValidation(4, Now);
        job.RecordValidated(3, 1);
        job.AwaitConfirmation();

        job.Confirm().ShouldBeTrue();
        (job.Status, job.ProcessedRows, job.SuccessRows).ShouldBe((ImportJobStatus.Processing, 1, 0));
        job.RecordProcessed(2, 1);
        job.Complete(Now);
        (job.Status, job.ProcessedRows, job.SuccessRows, job.FailedRows, job.IsFinished).ShouldBe((ImportJobStatus.Completed, 4, 2, 2, true));
        job.Cancel(Now).ShouldBeFalse();
    }

    [Fact]
    public void Fail_KeepsTheReason_AndRowsTrackTheirOutcome()
    {
        var job = Job();
        job.Fail("AUX-22010", new string('x', 600), Now);
        (job.Status, job.ErrorCode, job.ErrorMessage!.Length, job.CompletedAt).ShouldBe((ImportJobStatus.Failed, "AUX-22010", 500, (DateTimeOffset?)Now));

        var valid = new ImportJobRow(job.Id, 2, "{}", null);
        var invalid = new ImportJobRow(job.Id, 3, "{}", "{\"name\":[\"x\"]}");
        (valid.Status, invalid.Status).ShouldBe((ImportRowStatus.Valid, ImportRowStatus.Invalid));
        var entity = Guid.CreateVersion7();
        valid.Imported(entity);
        invalid.Failed("{}");
        (valid.Status, valid.EntityId, invalid.Status).ShouldBe((ImportRowStatus.Imported, (Guid?)entity, ImportRowStatus.Failed));
        new ImportType(Guid.CreateVersion7(), " Clienti ", "Client", Now).Name.ShouldBe("Clienti");
    }
}
