# F33 — Service folder templates, case document folders, ZIP download

Status: [~] in progress (template B-10, documents in folders + ZIP B-12, case usage B-14 done; template editor B-15) · Tasks: B-10, B-12, B-14, B-15 · **Not listed in the blueprint — added by the legacy analysis.**

## Legacy behaviour
- Entity `MembershipFolderTemplate` (MembershipId, Name, ParentId?, SortOrder, Children, Documents) — migration `20260602145759_AddMembershipFolderTemplate`; `UserDocument.FolderTemplateId` (FK SetNull).
- `IMembershipFolderService`: list by membership (ordered parent, sort order, name), add (name, parent, sort order = number of siblings), rename/update sort order, reorder, **recursive delete** (children too).
- **Template editor** (`Shared/FolderTree.razor` + `FolderTreeNode.razor` in edit mode) on `Admin/MembershipDetail`: "All documents" root, add root folder, add child, rename (Enter/Escape), delete with confirmation "DeleteFolderConfirm", expand/collapse.
- **Case usage** (`SubscriptionDetail`, status InProgress, when the membership has folders): folder tree in browse mode; selecting a folder filters the document grid and shows the uploader targeting that folder (`DocumentUploader FolderTemplateId`); grid adds a "Folder" column; per-row "move to folder" select showing full paths "A / B / C" (or "all documents" = no folder) → `MoveDocumentToFolderAsync`.
- **ZIP download** (download icon on root and on each folder, with confirmation "DownloadZipConfirm"): `DownloadFolderAsZipAsync(subscriptionId, rootFolderId?)` — includes documents of the subtree (or the whole case when root), paths per the fix on branch `fix/zip-download-folder` (`abf49b0`, part of the baseline, D-30): whole case → full folder paths; chosen folder F → files directly in F at the zip root and files in subfolders relative to F (`F/B/x` → `B/x`); files without folder at zip root, duplicate names suffixed `_1`, `_2`…, skips files missing in storage; file named `{folder}.zip` or `{membership}.zip`; empty → "FileNotFound" toast.
- Translation keys added by seed scripts `S_20260602_001`, `S_20260604_002`, `S_20260604_003`.

## Backend (B-10)
`cases.service_folders` (service, parent with cascade, name, sort order). API under `/api/v1/services/{id}/folders`
(read `cases.services.view`, write `cases.services.manage`): `GET` (tree order with depth and path `A / B / C`),
`POST` (name, parent; goes last), `PUT /order` (every sibling once), `PUT /{folderId}` (rename), `DELETE /{folderId}`
(the subtree); every write answers with the template. Names unique among siblings (case-insensitive), no `/` or `\`
(the ZIP keeps paths); at most 10 levels and 500 folders per service. B-12 links documents with `ON DELETE SET NULL`.

## Acceptance criteria
- [x] Service detail: folder template editor (add root/child, rename, reorder, delete recursive with confirmation). *(API B-10; editor B-15, reorder with move up/down buttons)*
- [x] Case documents tab: tree navigation filters documents; upload into selected folder; move document between folders (or to none); folder column with full path. *(B-14, ZIP of the case and of each folder with confirmation)*
- [x] ZIP download of the whole case or any folder subtree, same path and duplicate-name rules, streamed server-side, respects F10 visibility. *(built in a temporary file, then streamed)*
- [x] Template changes do not delete documents (documents of removed folders become "no folder").
- [x] Legacy folder templates and document-folder links migrated. *(templates: E-03 `ServiceCatalogStep`; document links: E-04 `DocumentsStep`, only folders of the case's service)*
