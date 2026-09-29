# F26 — Grid export

Status: [ ] not started · Tasks: P3-06, 4.33, 4.35 · Quirks: Q38

## Legacy behaviour
- `DataGrid EnableExport`: buttons Excel (actually CSV, UTF-8, all values quoted) and PDF (A4 "{AppName} - {Title}", header row, footer date). Exports **current page only** (Q38). Enabled on Admin Subscriptions and Membership detail.
- File name `{Title}_yyyyMMdd_HHmmss.{csv|pdf}`.

## Acceptance criteria
- [ ] Every data table offers export CSV, Excel (ClosedXML) and PDF of **all rows matching current filters/sort**, visible columns, localized headers.
- [ ] Same file naming convention; PDF title with tenant/app name and generation date.
- [ ] Exports respect permissions and F10 visibility.
- [ ] Above a row threshold export runs async with `ExportReady` notification and download link.
