"use client";

import Link from "next/link";
import { PlusIcon } from "lucide-react";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";

import { CustomFieldCell } from "@/components/custom-fields/custom-field-value";
import { DataTable } from "@/components/data-table/data-table";
import { gridViews } from "@/components/data-table/grid-views-menu";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import {
  applyLayout,
  customFieldColumns,
  type DataTableColumn,
} from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { UserAvatar } from "@/components/user-avatar";
import { tenantHref } from "@/lib/href";
import { useCan, useHasRole } from "@/lib/permissions";
import { ExportMenu } from "@/features/exports";

import {
  clientName,
  CLIENTS_GRID,
  useClientCustomFields,
  useClients,
  useTags,
  type ClientListItem,
} from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";
import { ClientStatusBadge } from "./client-status-badge";

const VIEWS = ["all", "mine"] as const;

/**
 * The client lists (F05, one page for every staff role): "my clients" (assigned to the calling employee) and "all
 * clients", filtered by name/surname, e-mail, user name, phone and status, sorted and paged by the API, with the
 * columns of the role's grid layout (F21) and the custom fields marked visible on grid (F20).
 */
export function ClientsTable({ tenant, title }: { tenant: string; title: string }) {
  const t = useTranslations();
  const isEmployee = useHasRole("Employee");
  const canCreate = useCan(DIRECTORY_PERMISSIONS.manageClients);
  const [view, setView] = useQueryState(
    "view",
    parseAsStringLiteral(VIEWS)
      .withDefault(isEmployee ? "mine" : "all")
      .withOptions({ history: "replace" }),
  );
  const table = useTableState([
    "fullName",
    "email",
    "userName",
    "phone",
    "status",
    "tagId",
  ] as const);
  const tags = useTags(tenant);
  const grid = useGridLayout(tenant, CLIENTS_GRID);
  const customFields = useClientCustomFields(tenant);
  const exportParams = {
    view,
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[fullName]": table.filters.fullName,
    "filter[email]": table.filters.email,
    "filter[userName]": table.filters.userName,
    "filter[phone]": table.filters.phone,
    "filter[status]": table.filters.status,
    "filter[tagId]": table.filters.tagId,
  };
  const query = useClients(tenant, exportParams);

  const columns: DataTableColumn<ClientListItem>[] = [
    {
      id: "lastName",
      header: t("Surname"),
      sortField: "lastName",
      hideable: false,
      mobile: "title",
      cell: (row) => (
        <span className="flex items-center gap-2">
          <UserAvatar
            userId={row.userId}
            name={clientName(row)}
            imageVersion={row.imageVersion}
            size="sm"
          />
          <Link
            href={tenantHref(tenant, `/clients/${row.id}`)}
            className="font-medium underline-offset-4 hover:underline"
          >
            {row.lastName}
          </Link>
        </span>
      ),
    },
    { id: "firstName", header: t("Name"), sortField: "fullName", cell: (row) => row.firstName },
    { id: "email", header: t("Email"), sortField: "email", cell: (row) => row.email ?? "—" },
    {
      id: "userName",
      header: t("Username"),
      sortField: "userName",
      cell: (row) => row.userName,
    },
    { id: "phone", header: t("Phone"), cell: (row) => row.phone ?? "—" },
    {
      id: "fiscalCode",
      header: t("app.clients.fiscalCode"),
      cell: (row) => row.fiscalCode ?? "—",
    },
    {
      id: "employee",
      header: t("app.clients.employee"),
      cell: (row) => row.employee?.fullName ?? "—",
    },
    { id: "status", header: t("Status"), cell: (row) => <ClientStatusBadge status={row.status} /> },
    {
      id: "canSignIn",
      header: t("app.clients.canSignIn"),
      cell: (row) =>
        row.canSignIn ? (
          <Badge variant="secondary">{t("Yes")}</Badge>
        ) : (
          <Badge variant="outline">{t("No")}</Badge>
        ),
    },
    ...customFieldColumns<ClientListItem>(customFields.definitions, (row, fields) => (
      <CustomFieldCell fields={fields} values={customFieldValues(row.customFields)} />
    )),
  ];

  // The System's layout for the user's role (F21): order and default visibility of the columns.
  const laid = applyLayout(columns, grid.layout);

  const toolbar = (
    <>
      <SearchFilter
        id="clients-search"
        label={t("Search")}
        placeholder={t("app.clients.searchName")}
        value={table.filters.fullName}
        onChange={(value) => table.setFilter("fullName", value)}
      />
      <SearchFilter
        id="clients-email"
        label={t("Email")}
        placeholder={t("Email")}
        value={table.filters.email}
        onChange={(value) => table.setFilter("email", value)}
      />
      <FilterSelect
        id="clients-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={[
          { value: "Active", label: t("Active") },
          { value: "Inactive", label: t("Inactive") },
        ]}
      />
      {(tags.data?.length ?? 0) > 0 ? (
        <FilterSelect
          id="clients-tag"
          label={t("app.clients.tags.filter")}
          value={table.filters.tagId}
          onChange={(value) => table.setFilter("tagId", value)}
          options={(tags.data ?? []).map((tag) => ({ value: tag.id, label: tag.name }))}
        />
      ) : null}
      {table.hasFilters ? (
        <Button type="button" variant="ghost" size="sm" onClick={table.clearFilters}>
          {t("ClearFilters")}
        </Button>
      ) : null}
    </>
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        {isEmployee ? (
          <div role="group" aria-label={title} className="inline-flex rounded-md border p-1">
            {VIEWS.slice()
              .reverse()
              .map((item) => (
                <Button
                  key={item}
                  type="button"
                  size="sm"
                  variant={view === item ? "secondary" : "ghost"}
                  aria-pressed={view === item}
                  onClick={() => void setView(item)}
                >
                  {t(item === "mine" ? "MyClients" : "AllClients")}
                </Button>
              ))}
          </div>
        ) : (
          <span />
        )}
        {canCreate ? (
          <Button asChild>
            <Link href={tenantHref(tenant, "/clients/new")}>
              <PlusIcon aria-hidden /> {t("AddClient")}
            </Link>
          </Button>
        ) : null}
      </div>
      <DataTable
        views={gridViews(tenant, CLIENTS_GRID, table)}
        // Remounted once the layout arrives, so its hidden columns become the initial column visibility.
        key={grid.layout ? "layout" : "default"}
        label={title}
        exportMenu={(columns) => (
          <ExportMenu tenant={tenant} source="clients" params={exportParams} columns={columns} />
        )}
        columns={laid.columns}
        initiallyHidden={grid.layout ? laid.hidden : ["userName", "fiscalCode"]}
        rows={query.data?.items}
        getRowId={(row) => row.id}
        totalCount={Number(query.data?.totalCount ?? 0)}
        page={table.page}
        pageSize={table.pageSize}
        sort={table.sort}
        onPageChange={table.setPage}
        onPageSizeChange={table.setPageSize}
        onSortChange={table.setSort}
        isLoading={query.isPending || grid.isPending}
        error={query.error}
        onRetry={() => void query.refetch()}
        toolbar={toolbar}
        filtered={table.hasFilters}
      />
    </div>
  );
}

/** The `customFields` JSON object of a client as a record (anything else is no values). */
export function customFieldValues(value: unknown): Readonly<Record<string, unknown>> {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}
