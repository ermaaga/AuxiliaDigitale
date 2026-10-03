using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Api.IntegrationTests.Cases;
using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Documents;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Hosting;

namespace Auxilia.Api.IntegrationTests.Documents;

/// <summary>
/// B-12 over HTTP (F14, F10, F33) on the local storage: multipart upload, lists, detail, download, metadata, folders,
/// ZIP, delete; private case documents hidden from employees in the list query; areas.
/// </summary>
public sealed class DocumentEndpointsTests(DocumentEndpointsTests.Factory factory) : IClassFixture<DocumentEndpointsTests.Factory>
{
    private const string Password = "A long Passw0rd for tests!";

    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, .. RandomNumberGenerator.GetBytes(2000)];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Administrator_UploadsReadsChangesZipsAndDeletes()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (clientId, _) = await CaseEndpointsTests.Factory.AddClientAsync();
        var (caseId, folder, subFolder) = await CaseWithFoldersAsync(admin, clientId);

        var ids = await UploadAsync(admin, clientId, caseId, folder, ("my scan.pdf", Pdf), ("notes.txt", "hello"u8.ToArray()));
        var sub = (await UploadAsync(admin, clientId, caseId, subFolder, ("deep.pdf", Pdf))).Single();
        var loose = (await UploadAsync(admin, clientId, null, null, ("loose.pdf", Pdf))).Single();

        var detail = await DetailAsync(admin, ids[0]);
        (detail.FileName, detail.Folder!.Path, detail.Case!.Id, detail.Status, detail.Size, detail.CanEdit, detail.CanManage)
            .ShouldBe(("my_scan.pdf", "Redditi", caseId, "Processing", Pdf.Length, true, true));
        detail.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Pdf)));

        using (var content = await SendAsync(HttpMethod.Get, $"/api/v1/documents/{ids[0]}/content", admin))
        {
            content.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await content.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Pdf);
            content.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        }

        using (var inline = await SendAsync(HttpMethod.Get, $"/api/v1/documents/{ids[0]}/content?inline=true", admin))
        {
            inline.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        }

        using (var text = await SendAsync(HttpMethod.Get, $"/api/v1/documents/{ids[1]}/content?inline=true", admin))
        {
            text.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        }

        var page = await ListAsync(admin, $"filter[clientId]={clientId}&sort=fileName");
        page.Items.Select(item => item.FileName).ShouldBe(["deep.pdf", "loose.pdf", "my_scan.pdf", "notes.txt"]);
        (await ListAsync(admin, $"filter[caseId]={caseId}&filter[folderId]={folder}")).TotalCount.ShouldBe(2);

        // F33 ZIP: folder paths kept; one folder: its files at the root, subfolders relative.
        (await ZipEntriesAsync(admin, $"caseId={caseId}")).ShouldBe(["Redditi/my_scan.pdf", "Redditi/notes.txt", "Redditi/2025/deep.pdf"], ignoreOrder: true);
        (await ZipEntriesAsync(admin, $"caseId={caseId}&folderId={folder}")).ShouldBe(["my_scan.pdf", "notes.txt", "2025/deep.pdf"], ignoreOrder: true);

        using (var renamed = await SendAsync(HttpMethod.Put, $"/api/v1/documents/{ids[0]}", admin, new UpdateDocumentRequest("Dichiarazione.docx", 2025, null, "anno 2025", null)))
        {
            renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync(Ct));
            var body = (await renamed.Content.ReadFromJsonAsync<DocumentResponse>(Ct))!;
            (body.FileName, body.ReferenceYear, body.Description).ShouldBe(("Dichiarazione.pdf", 2025, "anno 2025"));
        }

        using (var moved = await SendAsync(HttpMethod.Put, $"/api/v1/documents/{sub}/folder", admin, new MoveDocumentRequest(null)))
        {
            (await moved.Content.ReadFromJsonAsync<DocumentResponse>(Ct))!.Folder.ShouldBeNull();
        }

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Put, $"/api/v1/documents/{loose}/folder", admin, new MoveDocumentRequest(folder)),
            HttpStatusCode.BadRequest, EventCodes.Documents.DocumentInvalid);

        (await SendAsync(HttpMethod.Delete, $"/api/v1/documents/{loose}", admin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/documents/{loose}", admin), HttpStatusCode.NotFound, EventCodes.Documents.DocumentNotFound);
    }

    [Fact]
    public async Task Uploads_AreChecked()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var (clientId, _) = await CaseEndpointsTests.Factory.AddClientAsync();

        await ShouldHaveCodeAsync(await UploadResponseAsync(admin, clientId, null, null, ("virus.exe", Pdf)), HttpStatusCode.BadRequest, EventCodes.Documents.FileTypeNotAllowed);
        await ShouldHaveCodeAsync(await UploadResponseAsync(admin, clientId, null, null, ("fake.pdf", "MZ binary"u8.ToArray())), HttpStatusCode.BadRequest,
            EventCodes.Documents.FileContentMismatch);
        await ShouldHaveCodeAsync(await UploadResponseAsync(admin, Guid.NewGuid(), null, null, ("a.pdf", Pdf)), HttpStatusCode.BadRequest, EventCodes.Documents.DocumentInvalid);

        await UploadAsync(admin, clientId, null, null, ("same.pdf", Pdf));
        await ShouldHaveCodeAsync(await UploadResponseAsync(admin, clientId, null, null, ("SAME.pdf", Pdf)), HttpStatusCode.Conflict, EventCodes.Documents.DocumentNameTaken);
    }

    [Fact]
    public async Task Employees_DoNotSeePrivateCaseDocuments_OfSpecializationsTheyLack()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var employee = await SignInAsync(TenantRole.Employee);
        var (clientId, _) = await CaseEndpointsTests.Factory.AddClientAsync();
        var hidden = await CaseEndpointsTests.Factory.AddSpecializationAsync(isPrivate: true, member: null);
        var privateCase = await OpenCaseAsync(admin, clientId, await ServiceAsync(admin, hidden));
        var publicCase = await OpenCaseAsync(admin, clientId, await ServiceAsync(admin, null));

        var hiddenDocument = (await UploadAsync(admin, clientId, privateCase, null, ("secret.pdf", Pdf))).Single();
        var visibleDocument = (await UploadAsync(admin, clientId, publicCase, null, ("public.pdf", Pdf))).Single();

        (await ListAsync(employee, $"filter[clientId]={clientId}")).Items.Select(item => item.Id).ShouldBe([visibleDocument]);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/documents/{hiddenDocument}", employee), HttpStatusCode.NotFound, EventCodes.Documents.DocumentNotFound);
        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Get, $"/api/v1/documents/zip?caseId={privateCase}", employee), HttpStatusCode.NotFound, EventCodes.Cases.CaseNotFound);
        (await ListAsync(admin, $"filter[clientId]={clientId}")).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Areas_AreManagedByAdministrators()
    {
        var admin = await SignInAsync(TenantRole.Administrator);
        var employee = await SignInAsync(TenantRole.Employee);
        var name = "Area " + Guid.NewGuid().ToString("N")[..6];

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/document-areas", admin, new CreateDocumentAreaRequest(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var area = (await created.Content.ReadFromJsonAsync<DocumentAreaResponse[]>(Ct))!.Single(item => item.Name == name);

        await ShouldHaveCodeAsync(await SendAsync(HttpMethod.Post, "/api/v1/document-areas", admin, new CreateDocumentAreaRequest(name.ToUpperInvariant())),
            HttpStatusCode.Conflict, EventCodes.Documents.DocumentAreaNameTaken);
        (await SendAsync(HttpMethod.Post, "/api/v1/document-areas", employee, new CreateDocumentAreaRequest("x"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var listed = await SendAsync(HttpMethod.Get, "/api/v1/document-areas", employee))
        {
            (await listed.Content.ReadFromJsonAsync<DocumentAreaResponse[]>(Ct))!.ShouldContain(item => item.Id == area.Id);
        }

        (await SendAsync(HttpMethod.Put, $"/api/v1/document-areas/{area.Id}", admin, new UpdateDocumentAreaRequest(name, false))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<string> SignInAsync(TenantRole role)
    {
        var (_, userName) = await factory.AddUserAsync([role], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    private async Task<Guid> ServiceAsync(string token, Guid? specialization)
    {
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/services", token,
            new CreateServiceRequest("Docs service " + Guid.NewGuid().ToString("N")[..8], null, 10m, 30, null, specialization));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<ServiceResponse>(Ct))!.Id;
    }

    private async Task<Guid> OpenCaseAsync(string token, Guid clientId, Guid serviceId)
    {
        using var opened = await SendAsync(HttpMethod.Post, "/api/v1/cases", token, new OpenCaseRequest(clientId, serviceId, null, null, null, null, null));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Ct));
        return (await opened.Content.ReadFromJsonAsync<CaseResponse>(Ct))!.Id;
    }

    private async Task<(Guid CaseId, Guid Folder, Guid SubFolder)> CaseWithFoldersAsync(string token, Guid clientId)
    {
        var service = await ServiceAsync(token, null);
        using var root = await SendAsync(HttpMethod.Post, $"/api/v1/services/{service}/folders", token, new CreateServiceFolderRequest("Redditi", null));
        var folder = (await root.Content.ReadFromJsonAsync<ServiceFolderResponse[]>(Ct))!.Single().Id;
        using var child = await SendAsync(HttpMethod.Post, $"/api/v1/services/{service}/folders", token, new CreateServiceFolderRequest("2025", folder));
        var subFolder = (await child.Content.ReadFromJsonAsync<ServiceFolderResponse[]>(Ct))!.Single(item => item.Name == "2025").Id;
        return (await OpenCaseAsync(token, clientId, service), folder, subFolder);
    }

    private async Task<HttpResponseMessage> UploadResponseAsync(string token, Guid clientId, Guid? caseId, Guid? folderId, params (string Name, byte[] Content)[] files)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(clientId.ToString()), "clientId" },
        };
        if (caseId is { } @case)
        {
            form.Add(new StringContent(@case.ToString()), "caseId");
        }

        if (folderId is { } folder)
        {
            form.Add(new StringContent(folder.ToString()), "folderId");
        }

        foreach (var (name, content) in files)
        {
            form.Add(new ByteArrayContent(content), "files", name);
        }

        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    private async Task<IReadOnlyList<Guid>> UploadAsync(string token, Guid clientId, Guid? caseId, Guid? folderId, params (string Name, byte[] Content)[] files)
    {
        using var response = await UploadResponseAsync(token, clientId, caseId, folderId, files);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<UploadDocumentsResponse>(Ct))!.DocumentIds;
    }

    private async Task<DocumentResponse> DetailAsync(string token, Guid id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/documents/{id}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DocumentResponse>(Ct))!;
    }

    private async Task<PagedResponse<DocumentListItemResponse>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/documents?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResponse<DocumentListItemResponse>>(Ct))!;
    }

    private async Task<string[]> ZipEntriesAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/v1/documents/zip?" + query, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await using var zip = await response.Content.ReadAsStreamAsync(Ct);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

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

    /// <summary>The API with the local storage in a folder of its own, removed at the end.</summary>
    public sealed class Factory : AuthEndpointsTests.Factory
    {
        private readonly string storage = Path.Combine(Path.GetTempPath(), "auxilia-documents-" + Guid.NewGuid().ToString("N"));

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(storage))
            {
                Directory.Delete(storage, recursive: true);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Storage:Local:RootPath", storage);
        }
    }
}
