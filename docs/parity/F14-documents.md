# F14 — Documents

Status: [~] in progress (storage B-11 and documents API B-12 done; pages B-13) · Tasks: B-11, B-12, B-13 · Quirks: Q13, Q49, Q50, Q51

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

## Documents (B-12)
`documents.documents` (client, case `SET NULL`, folder `SET NULL`, name, storage key, content type, size, SHA-256,
reference year, area, description, custom fields, uploader, status `Processing` → `Available`/`Damaged`) and
`documents.document_areas` (managed list, name unique among the active ones; legacy values arrive with the import). API
(module `documents`): `GET /documents` (filters client, case, folder, client name, file name, description, uploader,
exact year, area; sorts as the legacy, default newest first), `GET /documents/{id}`, `GET /documents/{id}/content`
(`inline=true` only for PDF and images), `POST /documents` (multipart: up to 50 files with the same metadata; custom name
for one file only, original extension appended), `PUT /documents/{id}` (name without changing the extension, year,
area, description, custom fields), `PUT /documents/{id}/folder`, `DELETE /documents/{id}` (file deleted after the
commit), `GET /documents/zip?caseId=&folderId=`, `/document-areas` (list for staff, write `documents.areas.manage`).
Uploads are staged, committed inside the operation (removed again if it fails) and checked by the Worker
(`ProcessDocumentCommand`, queue `auxilia.documents`: checksum of the stored file), which pushes `DocumentProcessed` to the
uploader (Q13). Names unique per client, case and folder (Q51 fix).

## Acceptance criteria
- [x] Storage providers Local/FTP/Azure selectable by configuration (tenant setting instead of precedence), keys prefixed `tenants/{slug}/documents/`, SHA-256 computed (stored with the document, B-12).
- [ ] Multi-file drag & drop + clipboard paste + progress; max size per tenant (default 60 MB); whitelist + magic bytes.
- [x] Metadata rules: reference year ≥ current−10; custom name single-file only with extension kept; sanitization as legacy.
- [x] Duplicate names detected against existing documents of the same owner/case/folder.
- [x] Lists with the filters/sorts above, per client and global, access rules F10. *(API; pages B-13)*
- [ ] Detail drawer: preview (pdf/images), edit metadata, download, delete (confirm).
- [x] Async processing path via Worker notifies the **uploader** (`DocumentProcessed`) and refreshes lists. *(event; list refresh in B-13)*
- [ ] Areas become a managed lookup seeded from legacy distinct values. *(lookup and API B-12; seed from the legacy values with the import E-04)*
