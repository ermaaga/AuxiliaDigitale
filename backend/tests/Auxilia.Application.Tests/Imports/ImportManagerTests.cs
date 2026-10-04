using System.Text.Json;

using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Imports;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Imports;
using Auxilia.Contracts.Messages.V1.Imports;
using Auxilia.Diagnostics;
using Auxilia.Domain.Imports;
using Auxilia.SharedKernel.Results;

using NSubstitute;

namespace Auxilia.Application.Tests.Imports;

public sealed class ImportManagerTests : IAsyncDisposable
{
    private static readonly byte[] Workbook = [1, 2, 3];

    private readonly InMemoryImports data = new();
    private readonly FakeWorkbook workbook = new();
    private readonly FakeTarget target = new();
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly ManualTimeProvider clock = new();
    private readonly ImportManager manager;
    private readonly ImportTypeManager types;
    private readonly ImportQueryService query;

    public ImportManagerTests()
    {
        manager = new ImportManager(ManagerHarness.Runner(), data, workbook, [target], outbox, clock);
        types = new ImportTypeManager(ManagerHarness.Runner(), data, [target], clock);
        query = new ImportQueryService(data, workbook, [target]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private async Task<Guid> TypeAsync() => (await types.CreateAsync(new CreateImportTypeRequest(null, "thing"), Ct)).Value;

    private async Task<Guid> StartAsync(params ImportRow[] rows)
    {
        workbook.Sheet = new ImportSheet(rows, []);
        return (await manager.StartAsync(new StartImportRequest("March", await TypeAsync(), "things.xlsx", Workbook), Ct)).Value;
    }

    private static ImportRow Row(int number, string name) => new(number, new Dictionary<string, string> { ["name"] = name });

    private ImportJob Job(Guid id) => data.Jobs.Single(job => job.Id == id);

    [Fact]
    public async Task Types_DefaultToTheEntityName_AreUnique_AndOnlyKnownEntities()
    {
        var id = await TypeAsync();

        data.Types.Single().ShouldSatisfyAllConditions(type => type.Name.ShouldBe("Thing"), type => type.TargetEntity.ShouldBe("Thing"));
        (await types.CreateAsync(new CreateImportTypeRequest("thing", "Thing"), Ct)).Error!.ValidationErrors["name"].ShouldBe(["validation.imports.typeNameTaken"]);
        (await types.CreateAsync(new CreateImportTypeRequest(null, "Training"), Ct)).Error!.ValidationErrors["targetEntity"].ShouldBe(["validation.imports.entity"]);
        (await query.TemplateAsync(id, Ct)).Value.FileName.ShouldBe("Thing_template.xlsx");
        workbook.TemplateFields.ShouldBe(target.Fields);
        query.Entities().ShouldHaveSingleItem().Fields.Select(field => (field.Key, field.Required)).ShouldBe([("name", true), ("note", false)]);
    }

    [Fact]
    public async Task Start_ChecksNameTypeAndFile_ThenQueuesTheValidation()
    {
        var typeId = await TypeAsync();

        var invalid = await manager.StartAsync(new StartImportRequest(" ", Guid.CreateVersion7(), "things.csv", Workbook), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["name", "file", "importTypeId"], ignoreOrder: true);
        (await manager.StartAsync(new StartImportRequest("Big", typeId, "big.xlsx", new byte[ImportLimits.MaxFileBytes + 1]), Ct))
            .Error!.ValidationErrors["file"].ShouldBe(["validation.imports.fileSize"]);

        var id = (await manager.StartAsync(new StartImportRequest("March", typeId, "C:\\tmp\\things.xlsx", Workbook), Ct)).Value;

        (Job(id).Status, Job(id).FileName, Job(id).FileContent).ShouldBe((ImportJobStatus.Pending, "things.xlsx", Workbook));
        await outbox.Received(1).EnqueueAsync(Arg.Any<IOperationScope>(), Arg.Is<ValidateImportCommand>(command => command.ImportId == id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_StoresEveryRowWithItsErrors_AndWaitsForConfirmation()
    {
        var id = await StartAsync(Row(2, "a"), Row(3, "bad"), Row(4, "c"));

        (await manager.ValidateAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        var job = Job(id);
        (job.Status, job.TotalRows, job.SuccessRows, job.FailedRows, job.FileContent).ShouldBe((ImportJobStatus.AwaitingConfirmation, 3, 2, 1, (byte[]?)null));
        data.Rows.Select(row => (row.RowNumber, row.Status)).ShouldBe([(2, ImportRowStatus.Valid), (3, ImportRowStatus.Invalid), (4, ImportRowStatus.Valid)]);
        var rows = (await query.RowsAsync(id, "invalid", 1, 25, Ct)).Value;
        var invalid = rows.Items.ShouldHaveSingleItem();
        (invalid.RowNumber, invalid.Values["name"], invalid.Errors!["name"][0]).ShouldBe((3, "bad", "validation.imports.taken"));
        (await query.GetAsync(id, Ct)).Value.ShouldSatisfyAllConditions(
            response => response.Status.ShouldBe("AwaitingConfirmation"), response => response.Progress.ShouldBe(100));

        // A redelivered message does nothing.
        (await manager.ValidateAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        data.Rows.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("columns")]
    [InlineData("empty")]
    [InlineData("tooMany")]
    public async Task Validate_AnUnusableFile_FailsTheImport(string problem)
    {
        var id = await StartAsync(Row(2, "a"));
        workbook.Sheet = problem switch
        {
            "unreadable" => null,
            "columns" => new ImportSheet([Row(2, "a")], ["name"]),
            "empty" => new ImportSheet([], []),
            _ => new ImportSheet([.. Enumerable.Range(2, ImportLimits.MaxRows + 1).Select(number => Row(number, "x"))], []),
        };

        (await manager.ValidateAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        (Job(id).Status, Job(id).ErrorCode, Job(id).FileContent).ShouldBe((ImportJobStatus.Failed, "AUX-22010", (byte[]?)null));
        data.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmAndProcess_ImportTheValidRowsOneByOne_AndCountWhatFailed()
    {
        var id = await StartAsync(Row(2, "a"), Row(3, "bad"), Row(4, "boom"), Row(5, "d"));
        await manager.ValidateAsync(id, Ct);
        (await manager.CancelAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportNotFound);

        (await manager.ConfirmAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.ConfirmAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportStatusInvalid);
        await outbox.Received(1).EnqueueAsync(Arg.Any<IOperationScope>(), Arg.Is<ProcessImportCommand>(command => command.ImportId == id), Arg.Any<CancellationToken>());

        (await manager.ProcessAsync(id, Ct)).IsSuccess.ShouldBeTrue();

        var job = Job(id);
        (job.Status, job.SuccessRows, job.FailedRows, job.ProcessedRows).ShouldBe((ImportJobStatus.Completed, 2, 2, 4));
        target.Imported.ShouldBe(["a", "d"]);
        data.Rows.Select(row => row.Status).ShouldBe([ImportRowStatus.Imported, ImportRowStatus.Invalid, ImportRowStatus.Failed, ImportRowStatus.Imported]);
        data.Rows[0].EntityId.ShouldNotBeNull();
        JsonSerializer.Deserialize<Dictionary<string, string[]>>(data.Rows[2].Errors!)!["row"].ShouldBe(["errors.AUX-14022"]);

        (await manager.ProcessAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        target.Imported.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Cancel_DiscardsTheRows_AndOnlyFinishedImportsAreDeleted()
    {
        var id = await StartAsync(Row(2, "a"));
        (await manager.DeleteAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportStatusInvalid);
        await manager.ValidateAsync(id, Ct);

        (await manager.CancelAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        (Job(id).Status, data.Rows.Count).ShouldBe((ImportJobStatus.Cancelled, 0));
        (await manager.ConfirmAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportStatusInvalid);

        var typeId = Job(id).ImportTypeId;
        (await types.DeleteAsync(typeId, Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportTypeInUse);
        (await manager.DeleteAsync(id, Ct)).IsSuccess.ShouldBeTrue();
        data.Jobs.ShouldBeEmpty();
        (await types.DeleteAsync(typeId, Ct)).IsSuccess.ShouldBeTrue();
        (await types.DeleteAsync(typeId, Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportTypeNotFound);
    }

    [Fact]
    public async Task Queries_PageTheImports_AndRejectAnUnknownRowStatus()
    {
        var id = await StartAsync(Row(2, "a"));

        var list = await query.ListAsync(0, 1000, Ct);
        (list.Page, list.PageSize, list.TotalCount, list.Items.Single().Progress, list.Items.Single().ImportTypeName).ShouldBe((1, 100, 1L, 0, "Thing"));
        (await query.RowsAsync(id, "deleted", 1, 25, Ct)).Error!.ValidationErrors["status"].ShouldBe(["validation.imports.rowStatus"]);
        (await query.RowsAsync(Guid.CreateVersion7(), null, 1, 25, Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportNotFound);
        (await query.GetAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportNotFound);
        (await query.TemplateAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Imports.ImportTypeNotFound);
    }

    private sealed class FakeWorkbook : IImportWorkbook
    {
        public ImportSheet? Sheet { get; set; }

        public IReadOnlyList<ImportField>? TemplateFields { get; private set; }

        public ImportSheet? Read(byte[] content, IReadOnlyList<ImportField> fields) => Sheet;

        public byte[] Template(string sheetName, IReadOnlyList<ImportField> fields)
        {
            TemplateFields = fields;
            return [9];
        }
    }

    /// <summary>"bad" is invalid; "boom" fails when imported.</summary>
    private sealed class FakeTarget : IImportTarget
    {
        public List<string> Imported { get; } = [];

        public string Entity => "Thing";

        public IReadOnlyList<ImportField> Fields { get; } = [new("name", "Name", Required: true), new("note", "Note", Required: false)];

        public Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ImportRowErrors>>([.. rows.Select(row =>
            {
                var errors = new ImportRowErrors();
                if (row.Value("name") == "bad")
                {
                    errors.Add("name", ImportValues.Taken);
                }

                return errors;
            })]);

        public Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken)
        {
            if (row.Value("name") == "boom")
            {
                return Task.FromResult(Result.Failure<Guid>(Errors.Cases.CaseNotFound()));
            }

            Imported.Add(row.Value("name")!);
            return Task.FromResult(Result.Success(Guid.CreateVersion7()));
        }
    }
}

/// <summary>Imports in memory (the SQL is tested on PostgreSQL).</summary>
internal sealed class InMemoryImports : IImportDataFactory, IImportData
{
    public List<ImportType> Types { get; } = [];

    public List<ImportJob> Jobs { get; } = [];

    public List<ImportJobRow> Rows { get; } = [];

    public Task<IImportData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IImportData>(this);

    public Task<IReadOnlyList<ImportTypeRow>> TypesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ImportTypeRow>>([.. Types.OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(type => new ImportTypeRow(type.Id, type.Name, type.TargetEntity, type.CreatedAt, Jobs.Count(job => job.ImportTypeId == type.Id)))]);

    public Task<ImportType?> FindTypeAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Types.SingleOrDefault(type => type.Id == id));

    public Task<bool> TypeHasJobsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Jobs.Any(job => job.ImportTypeId == id));

    public Task<(IReadOnlyList<ImportJobListRow> Items, int Total)> JobsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var items = Jobs.OrderByDescending(job => job.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(ToRow).ToArray();
        return Task.FromResult<(IReadOnlyList<ImportJobListRow>, int)>((items, Jobs.Count));
    }

    public Task<ImportJobListRow?> JobAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Jobs.Where(job => job.Id == id).Select(ToRow).SingleOrDefault());

    public Task<ImportJob?> FindJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Jobs.SingleOrDefault(job => job.Id == id));

    public Task<(IReadOnlyList<ImportJobRowData> Items, int Total)> RowsAsync(
        Guid jobId, ImportRowStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var rows = Rows.Where(row => row.JobId == jobId && (status is null || row.Status == status)).OrderBy(row => row.RowNumber).ToArray();
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).Select(row => new ImportJobRowData(row.RowNumber, row.Status, row.Data, row.Errors, row.EntityId)).ToArray();
        return Task.FromResult<(IReadOnlyList<ImportJobRowData>, int)>((items, rows.Length));
    }

    public Task<IReadOnlyList<ImportJobRow>> ValidRowsAsync(Guid jobId, int afterRow, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ImportJobRow>>([.. Rows
            .Where(row => row.JobId == jobId && row.Status == ImportRowStatus.Valid && row.RowNumber > afterRow)
            .OrderBy(row => row.RowNumber)
            .Take(take)]);

    public Task DeleteRowsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        Rows.RemoveAll(row => row.JobId == jobId);
        return Task.CompletedTask;
    }

    public void Add(ImportType type) => Types.Add(type);

    public void Remove(ImportType type) => Types.Remove(type);

    public void Add(ImportJob job) => Jobs.Add(job);

    public void Remove(ImportJob job)
    {
        Jobs.Remove(job);
        Rows.RemoveAll(row => row.JobId == job.Id);
    }

    public void AddRows(IEnumerable<ImportJobRow> rows) => Rows.AddRange(rows);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ImportJobListRow ToRow(ImportJob job)
    {
        var type = Types.Single(item => item.Id == job.ImportTypeId);
        return new ImportJobListRow(
            job.Id, type.Id, type.Name, type.TargetEntity, job.Name, job.FileName, job.Status, job.TotalRows, job.ProcessedRows, job.SuccessRows,
            job.FailedRows, job.ErrorCode, job.ErrorMessage, job.CreatedAt, job.StartedAt, job.CompletedAt);
    }
}
