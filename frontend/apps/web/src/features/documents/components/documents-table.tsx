"use client";

import * as React from "react";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";

import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";

import { useDocumentAreas, useDocuments, type DocumentListItem } from "../api";
import { formatSize, referenceYears } from "../schemas/document";
import { DocumentDrawer } from "./document-drawer";
import { DocumentStatusBadge } from "./document-status-badge";

/**
 * The document lists (F14, one list for every staff role; the API applies F10): filters by client, file name,
 * reference year and area, sorted and paged by the API; a row opens the detail drawer. `clientId` restricts the
 * list to one client (client 360°), and hides the client column and filter.
 */
export function DocumentsTable({
  tenant,
  label,
  clientId,
}: {
  tenant: string;
  label: string;
  clientId?: string;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const format = useFormatter();
  const [selected, setSelected] = React.useState<string | undefined>();
  const table = useTableState(["clientName", "fileName", "referenceYear", "areaId"] as const);
  const areas = useDocumentAreas(tenant);
  const year = table.filters.referenceYear ? Number(table.filters.referenceYear) : undefined;
  const query = useDocuments(tenant, {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[clientId]": clientId,
    "filter[clientName]": clientId ? undefined : table.filters.clientName,
    "filter[fileName]": table.filters.fileName,
    "filter[referenceYear]": Number.isInteger(year) ? year : undefined,
    "filter[areaId]": table.filters.areaId,
  });

  const columns: DataTableColumn<DocumentListItem>[] = [
    {
      id: "fileName",
      header: t("FileName"),
      sortField: "fileName",
      hideable: false,
      mobile: "title",
      cell: (row) => (
        <span className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            className="text-left font-medium underline-offset-4 hover:underline"
            onClick={() => setSelected(row.id)}
          >
            {row.fileName}
          </button>
          <DocumentStatusBadge status={row.status} />
        </span>
      ),
    },
    ...(clientId
      ? []
      : [
          {
            id: "client",
            header: t("Client"),
            sortField: "client",
            cell: (row: DocumentListItem) => row.client.fullName,
          },
        ]),
    {
      id: "referenceYear",
      header: t("ReferenceYear"),
      sortField: "referenceYear",
      cell: (row) => row.referenceYear,
    },
    { id: "area", header: t("Area"), sortField: "area", cell: (row) => row.area?.name ?? "—" },
    {
      id: "case",
      header: t("app.documents.case"),
      cell: (row) => (row.case ? `${row.case.number} · ${row.case.serviceName}` : "—"),
    },
    { id: "description", header: t("Description"), cell: (row) => row.description ?? "—" },
    {
      id: "size",
      header: t("app.documents.size"),
      cell: (row) => formatSize(Number(row.size), locale),
    },
    {
      id: "uploadedBy",
      header: t("UploadedBy"),
      sortField: "uploadedBy",
      cell: (row) => row.uploadedBy?.fullName ?? "—",
    },
    {
      id: "uploadedAt",
      header: t("UploadDate"),
      sortField: "uploadedAt",
      mobile: "detail",
      cell: (row) =>
        format.dateTime(new Date(row.uploadedAt), { dateStyle: "medium", timeStyle: "short" }),
    },
  ];

  const toolbar = (
    <>
      {clientId ? null : (
        <SearchFilter
          id="documents-client"
          label={t("Client")}
          placeholder={t("app.documents.searchClient")}
          value={table.filters.clientName}
          onChange={(value) => table.setFilter("clientName", value)}
        />
      )}
      <SearchFilter
        id="documents-name"
        label={t("FileName")}
        placeholder={t("FileName")}
        value={table.filters.fileName}
        onChange={(value) => table.setFilter("fileName", value)}
      />
      <FilterSelect
        id="documents-year"
        label={t("ReferenceYear")}
        value={table.filters.referenceYear}
        onChange={(value) => table.setFilter("referenceYear", value)}
        options={referenceYears().map((item) => ({ value: String(item), label: String(item) }))}
      />
      <FilterSelect
        id="documents-area"
        label={t("Area")}
        value={table.filters.areaId}
        onChange={(value) => table.setFilter("areaId", value)}
        options={(areas.data ?? []).map((area) => ({ value: area.id, label: area.name }))}
      />
      {table.hasFilters ? (
        <Button type="button" variant="ghost" size="sm" onClick={table.clearFilters}>
          {t("ClearFilters")}
        </Button>
      ) : null}
    </>
  );

  return (
    <>
      <DataTable
        label={label}
        columns={columns}
        initiallyHidden={["description", "size"]}
        rows={query.data?.items}
        getRowId={(row) => row.id}
        totalCount={Number(query.data?.totalCount ?? 0)}
        page={table.page}
        pageSize={table.pageSize}
        sort={table.sort}
        onPageChange={table.setPage}
        onPageSizeChange={table.setPageSize}
        onSortChange={table.setSort}
        isLoading={query.isPending}
        error={query.error}
        onRetry={() => void query.refetch()}
        toolbar={toolbar}
        filtered={table.hasFilters}
      />
      <DocumentDrawer tenant={tenant} id={selected} onClose={() => setSelected(undefined)} />
    </>
  );
}
