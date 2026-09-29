# F07 — Clients overview + PDF

Status: [ ] not started · Tasks: 4.33, 4.35

## Legacy behaviour
- `Admin/ClientsOverview.razor` (`/admin/clients-overview`, not in sidebar). Loads all clients; collapsible filters: name contains, status (all/active/inactive), assigned employee; "Clear filters". Table: photo, client name, e-mail, phone, assigned employee (or "Not assigned"), status.
- "Export PDF" → `DocumentService.GenerateClientsOverviewPdf(filteredIds)` (QuestPDF, A4): title "{AppName} - Clients Overview", generated date, "Total Clients: N", table Name/Email/Phone/Assigned Employee/Status, page X of Y. File `ClientsOverview_yyyyMMdd_HHmmss.pdf`.

## Acceptance criteria
- [ ] Overview view reachable from `/clients` (and navigation) with filters name, status, assigned employee.
- [ ] Export PDF of **all rows matching filters** with the same content (title with app/tenant name, date, total, 5 columns, page numbers), localized, generated server-side with PDFsharp-MigraDoc.
- [ ] Multi-select rows → export only selected.
- [ ] Large exports run async and notify `ExportReady`.
