# F26 — Grid export

Status: [x] done (B-22, B-24) · Tasks: P3-07, B-22, B-24 · Quirks: Q38

## Legacy behaviour
- `DataGrid EnableExport`: buttons Excel (actually CSV, UTF-8, all values quoted) and PDF (A4 "{AppName} - {Title}", header row, footer date). Exports **current page only** (Q38). Enabled on Admin Subscriptions and Membership detail.
- File name `{Title}_yyyyMMdd_HHmmss.{csv|pdf}`.

## Acceptance criteria
- [x] Every data table offers export CSV, Excel (ClosedXML) and PDF of **all rows matching current filters/sort**, visible columns, localized headers. *(B-24: `ExportMenu` on the tenant tables)*
- [x] Same file naming convention; PDF title with tenant/app name and generation date. *(B-22)*
- [x] Exports respect permissions and F10 visibility. *(B-22: each list's own query service)*
- [x] Above a row threshold export runs async with `ExportReady` notification and download link. *(B-22: > 2000 rows → Worker, notification `export.ready`; download page B-24)*
