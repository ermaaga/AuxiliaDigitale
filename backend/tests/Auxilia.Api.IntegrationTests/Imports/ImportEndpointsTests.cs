using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Imports;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Imports;

using ClosedXML.Excel;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Imports;

/// <summary>
/// F19 over HTTP (D-18, D-21): import types and template, upload, validation and import (run here in a tenant scope as
/// the Worker would), preview of the rows, cancel and delete; only a tenant-scoped platform token gets in.
/// </summary>
public sealed class ImportEndpointsTests : IClassFixture<PlatformIdentityTests.Factory>
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly PlatformIdentityTests.Factory factory;

    public ImportEndpointsTests(PlatformIdentityTests.Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Services_AreValidated_Previewed_AndImported()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var entities = await SendAsync(HttpMethod.Get, "/api/v1/imports/entities", token);
        (await entities.Content.ReadFromJsonAsync<ImportEntityResponse[]>(Ct))!.Select(entity => entity.Entity).ShouldBe(["Case", "Client", "Employee", "Service"]);

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/imports/types", token, new CreateImportTypeRequest("Servizi " + suffix, "Service"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var typeId = (await created.Content.ReadFromJsonAsync<CreateImportTypeResponse>(Ct))!.Id;

        using var template = await SendAsync(HttpMethod.Get, $"/api/v1/imports/types/{typeId}/template", token);
        template.Content.Headers.ContentType!.MediaType.ShouldBe(XlsxType);
        using (var book = new XLWorkbook(await template.Content.ReadAsStreamAsync(Ct)))
        {
            book.Worksheets.Single().Cell(1, 1).GetString().ShouldBe("name");
        }

        using var invalid = await UploadAsync(token, typeId, "services.csv", [1, 2, 3]);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(invalid)).ShouldBe("AUX-22007");

        using var upload = await UploadAsync(token, typeId, "services.xlsx", Workbook(
            ["Import A " + suffix, "10,50", "30"],
            ["Import B " + suffix, "abc", "30"],
            ["Import A " + suffix, "5", "10"]));
        upload.StatusCode.ShouldBe(HttpStatusCode.Accepted, await upload.Content.ReadAsStringAsync(Ct));
        var id = (await upload.Content.ReadFromJsonAsync<StartImportResponse>(Ct))!.Id;
        (await JobAsync(token, id)).Status.ShouldBe("Pending");

        await AsWorkerAsync(imports => imports.ValidateAsync(id, Ct));

        var validated = await JobAsync(token, id);
        (validated.Status, validated.TotalRows, validated.SuccessRows, validated.FailedRows, validated.Progress).ShouldBe(("AwaitingConfirmation", 3, 1, 2, 100));
        using var invalidRows = await SendAsync(HttpMethod.Get, $"/api/v1/imports/{id}/rows?status=Invalid", token);
        var rows = (await invalidRows.Content.ReadFromJsonAsync<PagedResponse<ImportRowResponse>>(Ct))!;
        rows.Items.Select(row => (row.RowNumber, row.Errors!.Keys.Single())).ShouldBe([(3, "price"), (4, "name")]);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/imports/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, $"/api/v1/imports/{id}/confirm", token)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await JobAsync(token, id)).Status.ShouldBe("Processing");

        await AsWorkerAsync(imports => imports.ProcessAsync(id, Ct));

        var done = await JobAsync(token, id);
        (done.Status, done.SuccessRows, done.FailedRows, done.Progress).ShouldBe(("Completed", 1, 2, 100));
        using var imported = await SendAsync(HttpMethod.Get, $"/api/v1/imports/{id}/rows?status=Imported", token);
        (await imported.Content.ReadFromJsonAsync<PagedResponse<ImportRowResponse>>(Ct))!.Items.ShouldHaveSingleItem().EntityId.ShouldNotBeNull();

        (await SendAsync(HttpMethod.Delete, $"/api/v1/imports/types/{typeId}", token)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/imports/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/v1/imports/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/imports/types/{typeId}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AnUnreadableFile_FailsTheImport_AndAWaitingImportCanBeCancelled()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/imports/types", token, new CreateImportTypeRequest("Pratiche " + Guid.NewGuid().ToString("N")[..8], "Case"));
        var typeId = (await created.Content.ReadFromJsonAsync<CreateImportTypeResponse>(Ct))!.Id;

        using var broken = await UploadAsync(token, typeId, "broken.xlsx", "not a workbook"u8.ToArray());
        var brokenId = (await broken.Content.ReadFromJsonAsync<StartImportResponse>(Ct))!.Id;
        await AsWorkerAsync(imports => imports.ValidateAsync(brokenId, Ct));
        var failed = await JobAsync(token, brokenId);
        (failed.Status, failed.ErrorCode).ShouldBe(("Failed", "AUX-22010"));

        using var upload = await UploadAsync(token, typeId, "cases.xlsx", Workbook(["NOBODY00A00A000A", "Nessuno", "2026-01-01"], header: ["clientFiscalCode", "service", "startedOn"]));
        var id = (await upload.Content.ReadFromJsonAsync<StartImportResponse>(Ct))!.Id;
        await AsWorkerAsync(imports => imports.ValidateAsync(id, Ct));
        (await JobAsync(token, id)).FailedRows.ShouldBe(1);

        (await SendAsync(HttpMethod.Post, $"/api/v1/imports/{id}/cancel", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await JobAsync(token, id)).Status.ShouldBe("Cancelled");
        (await SendAsync(HttpMethod.Post, $"/api/v1/imports/{id}/cancel", token)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var list = await SendAsync(HttpMethod.Get, "/api/v1/imports?pageSize=100", token);
        (await list.Content.ReadFromJsonAsync<PagedResponse<ImportJobResponse>>(Ct))!.Items.Select(item => item.Id).ShouldContain(id);
    }

    [Fact]
    public async Task OnlyAPlatformTokenOfTheTenantGetsIn()
    {
        (await factory.CreateClient().GetAsync(new Uri("/api/v1/imports/types", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static byte[] Workbook(params string[][] rows) => Workbook(rows, ["name", "price", "durationDays"]);

    private static byte[] Workbook(string[] row, string[] header) => Workbook([row], header);

    private static byte[] Workbook(string[][] rows, string[] header)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Data");
        for (var column = 0; column < header.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = header[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                sheet.Cell(row + 2, column + 1).Value = rows[row][column];
            }
        }

        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>What the Worker handler does, in a scope of tenant A.</summary>
    private async Task AsWorkerAsync(Func<IImportManager, Task<SharedKernel.Results.Result>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(ApiDatabase.TenantA, Ct);
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
        (await work(scope.ServiceProvider.GetRequiredService<IImportManager>())).IsSuccess.ShouldBeTrue();
    }

    private async Task<ImportJobResponse> JobAsync(string token, Guid id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/imports/{id}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ImportJobResponse>(Ct))!;
    }

    private async Task<HttpResponseMessage> UploadAsync(string token, Guid typeId, string fileName, byte[] content)
    {
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Import " + fileName), "name" },
            { new StringContent(typeId.ToString()), "importTypeId" },
        };
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(XlsxType);
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/imports") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
}
