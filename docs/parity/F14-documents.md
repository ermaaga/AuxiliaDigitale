# F14 — Documents

Status: [~] in progress (storage B-11 done; documents B-12, pages B-13) · Tasks: B-11, B-12, B-13 · Quirks: Q13, Q49, Q50, Q51

## Legacy behaviour
Entity `UserDocument`: UserId (client), FileName, FilePath, FileType, FileSize, UploadedByUserId, UploadedAt, Description, ReferenceYear, Area (free text), SubscriptionId?, FolderTemplateId?.

**Storage** (`IDocumentStorageService`): FTP if `FtpStorage:Host` set, else Azure Blob if `AzureStorage:ConnectionString` set, else local folder `DocumentStorage:Path` (`uploads/documents`). Stored name `{guid}_{fileName}`.
**Upload** (`DocumentUploader` + `DocumentManager.UploadDocumentAsync`):
- Collapsible card; select multiple files (≤200), drag & drop, **paste from clipboard** (JS `setupPasteHandler`); per-file max size = `RabbitMQ:MaxMessageSizeMB` (Q49); duplicate name check within the batch.
- Metadata: custom file name (only for a single file; original extension appended if missing), reference year* (default current, ≥ current−10), area (free text, read-only when a default area is passed), description (optional). Case and folder passed by context.
- File name sanitized (spaces→`_`, quotes and invalid chars removed).
- If `UseQueueForDocuments`: bytes cached 30 min, `DocumentUploadMessage` sent to RabbitMQ queue `document-uploads`, handler saves file + row and pushes SignalR `ReceiveNotification` (Q13). Else synchronous save.
**Lists**: Admin `/admin/documents` (all, or `?clientId=` with "Documents for: surname name" + uploader + back to clients), grid client, file name, reference year, area, description, size, uploaded by, upload date; filters client, file name, year (exact), area, description, uploaded by; sort client, file name, year, area, uploaded by, uploaded at (default uploaded desc); actions download, edit, delete (confirm). Page refreshes on SignalR `ReceiveNotification`.
Employee `/employee/documents` (module `Documents`): same with access filter (F10); edit only when the client is assigned to me; delete only when `CanManageDocument`; "back to detail" when client is mine.
**Detail** `/…/documents/{id}`: client, edit file name (without extension; extension preserved; sanitized), reference year (≥ current−10), area, description.
**Download**: bytes → browser; "FileNotFound" toast when storage returns nothing.
**Delete**: removes file from storage and row.

## Storage (B-11)
Port `IFileStorage` with adapters `local` (`Storage:Local:RootPath`), `ftp` (FluentFTP, explicit FTPS by default) and
`azure-blob`; the provider is the tenant setting `documents.storage.provider`, the connection the settings
`documents.storage.ftp.*` / `documents.storage.azure.*` (password and connection string encrypted). `Documents.Public.IFileStore`:
`StageAsync` (name sanitised as Q51, whitelist of types with magic-byte check, size ≤ `documents.maxUploadMb` while
streaming, SHA-256) writes `tenants/{slug}/staging/{id}`; `CommitAsync` moves it to `tenants/{slug}/{area}/{yyyy}/{MM}/{id}.{ext}`
after the transaction; read and delete only accept keys of the current tenant. Rejections are security events
`AUX-29028`/`AUX-29029`. Staging leftovers: storage lifecycle rule (or the `documents.staging-cleanup` job) with B-12.
Change from the legacy: the provider is chosen per tenant, not by which configuration exists first.

## Acceptance criteria
- [x] Storage providers Local/FTP/Azure selectable by configuration (tenant setting instead of precedence), keys prefixed `tenants/{slug}/documents/`, SHA-256 computed (stored with the document, B-12).
- [ ] Multi-file drag & drop + clipboard paste + progress; max size per tenant (default 60 MB); whitelist + magic bytes.
- [ ] Metadata rules: reference year ≥ current−10; custom name single-file only with extension kept; sanitization as legacy.
- [ ] Duplicate names detected against existing documents of the same owner/case/folder.
- [ ] Lists with the filters/sorts above, per client and global, access rules F10.
- [ ] Detail drawer: preview (pdf/images), edit metadata, download, delete (confirm).
- [ ] Async processing path via Worker notifies the **uploader** (`DocumentProcessed`) and refreshes lists.
- [ ] Areas become a managed lookup seeded from legacy distinct values.
