using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Scheduling;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Reporting;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Tenancy;

using ClosedXML.Excel;

namespace Auxilia.Api.IntegrationTests.Reporting;

/// <summary>
/// B-22 over HTTP (F26, F07): every row of a list's filters as CSV, Excel or PDF with translated headers, selected ids,
/// the list's own permission, unknown lists and bad requests. Queued exports are covered by the Application tests.
/// </summary>
public sealed class ExportEndpointsTests(AppointmentEndpointsTests.Factory factory) : IClassFixture<AppointmentEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_ExportsTheFilteredClients_AsCsv_AndOnlyTheSelectedOnes()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (first, _) = await factory.AddClientAsync(null, Password);
        var (second, _) = await factory.AddClientAsync(null, Password);

        using var response = await SendAsync(admin, "/api/v1/exports/clients?format=csv&columns=lastName,firstName,status&language=it&filter[lastName]=Ferri&pageSize=1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldMatch(@"^[A-Za-z_]+_\d{8}_\d{6}\.csv$");
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Ct));
        var lines = text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        // Translated headers: Italian once LocalizationEndpointsTests has seeded tenant A, otherwise the fallback.
        lines[0].ShouldBeOneOf("\"Surname\";\"Name\";\"Status\"", "\"Cognome\";\"Nome\";\"Stato\"");
        // pageSize of the list is ignored: every client named Ferri is there.
        lines.Count(line => line.StartsWith("\"Ferri\";\"Marco\"", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(2);

        using var selected = await SendAsync(admin, $"/api/v1/exports/clients?format=csv&columns=lastName&ids={first}");
        Encoding.UTF8.GetString(await selected.Content.ReadAsByteArrayAsync(Ct)).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2);
        second.ShouldNotBe(first);
    }

    [Fact]
    public async Task Services_ExportAsExcelAndPdf()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var name = "Export " + Guid.NewGuid().ToString("N")[..8];
        using (var created = await PostAsync(admin, "/api/v1/services", new CreateServiceRequest(name, null, 99.5m, 30, null, null)))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var excel = await SendAsync(admin, $"/api/v1/exports/services?format=xlsx&language=en&filter[name]={name}");
        excel.StatusCode.ShouldBe(HttpStatusCode.OK, await excel.Content.ReadAsStringAsync(Ct));
        using var workbook = new XLWorkbook(new MemoryStream(await excel.Content.ReadAsByteArrayAsync(Ct)));
        var sheet = workbook.Worksheets.Single();
        sheet.Cell(4, 1).GetString().ShouldBe("Name");
        sheet.Cell(5, 1).GetString().ShouldBe(name);
        sheet.Cell(5, 5).GetString().ShouldBe("99.50 EUR");

        using var pdf = await SendAsync(admin, $"/api/v1/exports/services?format=pdf&filter[name]={name}");
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync(Ct))[..5]).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task EachListKeepsItsPermission_AndBadRequestsAreFieldErrors()
    {
        var employee = await SignInAsync(TenantRole.Employee);
        var admin = await SignInAsync(TenantRole.Administrator);

        await ShouldHaveCodeAsync(await SendAsync(employee, "/api/v1/exports/employees?format=csv"), HttpStatusCode.Forbidden, EventCodes.Identity.PermissionDenied);
        await ShouldHaveCodeAsync(await SendAsync(admin, "/api/v1/exports/nothing?format=csv"), HttpStatusCode.NotFound, EventCodes.Audit.ExportSourceNotFound);
        await ShouldHaveCodeAsync(await SendAsync(admin, "/api/v1/exports/clients?format=docx"), HttpStatusCode.BadRequest, EventCodes.Audit.ExportInvalid);
        await ShouldHaveCodeAsync(await SendAsync(admin, "/api/v1/exports/clients?columns=password"), HttpStatusCode.BadRequest, EventCodes.Audit.ExportInvalid);

        var sources = await (await SendAsync(employee, "/api/v1/exports/sources")).Content.ReadFromJsonAsync<ExportSourceResponse[]>(Ct);
        sources.ShouldNotBeNull();
        sources.Select(source => source.Key).ShouldContain("clients");
        sources.Select(source => source.Key).ShouldNotContain("employees");
        (await (await SendAsync(admin, "/api/v1/exports")).Content.ReadFromJsonAsync<ExportJobResponse[]>(Ct))!.ShouldBeEmpty();
    }

    private async Task<string> SignInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string path)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string path, object body)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, int code)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString().ShouldBe($"AUX-{code}");
        }
    }
}
