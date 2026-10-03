"use client";

import Link from "next/link";
import { parseAsBoolean, useQueryState } from "nuqs";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Label } from "@auxilia/ui/components/label";
import { Switch } from "@auxilia/ui/components/switch";

import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import { applyLayout, type DataTableColumn } from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { tenantHref } from "@/lib/href";
import { useCan, useHasRole } from "@/lib/permissions";

import { CASE_STATUSES, formatMoney, useCases, type CaseListItem } from "../api";
import { CASES_PERMISSIONS } from "../permissions";
import { CaseStatusBadge } from "./case-status-badge";
import { NewCaseDialog } from "./new-case-dialog";

/** The grid of the case lists (F21, `cases.cases`). */
export const CASES_GRID = "cases.cases";

/**
 * The case lists (F09, one page for every role; the API applies F10): filters by client, service and status, sorted
 * and paged by the API, newest first. Employees get the legacy toggles "show all practices" and "show completed"
 * (both off at first). `clientId` restricts the list to one client (client 360°) and fixes it in the quick creation.
 */
export function CasesTable({
  tenant,
  label,
  clientId,
  serviceId,
}: {
  tenant: string;
  label: string;
  clientId?: string;
  serviceId?: string;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const format = useFormatter();
  const isEmployee = useHasRole("Employee");
  const isAdministrator = useHasRole("Administrator");
  const employeeOnly = isEmployee && !isAdministrator;
  const canOpen = useCan(CASES_PERMISSIONS.manage);
  const [showAll, setShowAll] = useQueryState(
    "showAll",
    parseAsBoolean.withDefault(false).withOptions({ history: "replace" }),
  );
  const [showCompleted, setShowCompleted] = useQueryState(
    "showCompleted",
    parseAsBoolean
      .withDefault(clientId !== undefined || serviceId !== undefined)
      .withOptions({ history: "replace" }),
  );
  const table = useTableState(["clientName", "serviceName", "status"] as const);
  const grid = useGridLayout(tenant, CASES_GRID);
  const query = useCases(tenant, {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    showAll: employeeOnly ? showAll : undefined,
    showCompleted: employeeOnly ? showCompleted : undefined,
    "filter[clientId]": clientId,
    "filter[clientName]": clientId ? undefined : table.filters.clientName,
    "filter[serviceId]": serviceId,
    "filter[serviceName]": serviceId ? undefined : table.filters.serviceName,
    "filter[status]": table.filters.status,
  });
  const date = (value: string | null | undefined) =>
    value ? format.dateTime(new Date(value), { dateStyle: "medium" }) : "—";

  const columns: DataTableColumn<CaseListItem>[] = [
    {
      id: "number",
      header: t("Number"),
      sortField: "number",
      cell: (row) => (
        <Link
          href={tenantHref(tenant, `/cases/${row.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {row.number}
        </Link>
      ),
    },
    {
      id: "client",
      header: t("Client"),
      sortField: "client",
      hideable: false,
      mobile: "title",
      cell: (row) => row.client.fullName,
    },
    {
      id: "service",
      header: t("app.cases.service"),
      sortField: "service",
      cell: (row) => row.service.name,
    },
    {
      id: "startedOn",
      header: t("StartDate"),
      sortField: "startedOn",
      mobile: "detail",
      cell: (row) => date(row.startedOn),
    },
    {
      id: "expiresOn",
      header: t("EndDate"),
      sortField: "expiresOn",
      cell: (row) => date(row.expiresOn),
    },
    {
      id: "amountPaid",
      header: t("AmountPaid"),
      sortField: "amountPaid",
      cell: (row) => formatMoney(row.amountPaid, row.currency, locale),
    },
    {
      id: "status",
      header: t("Status"),
      cell: (row) => <CaseStatusBadge status={row.status} rejected={row.isRejected} />,
    },
    {
      id: "specialization",
      header: t("Specialization"),
      cell: (row) => row.specialization?.name ?? "—",
    },
  ];
  const visible = columns.filter(
    (column) => !(clientId && column.id === "client") && !(serviceId && column.id === "service"),
  );
  const laid = applyLayout(visible, grid.layout);

  const toolbar = (
    <>
      {clientId ? null : (
        <SearchFilter
          id="cases-client"
          label={t("Client")}
          placeholder={t("app.documents.searchClient")}
          value={table.filters.clientName}
          onChange={(value) => table.setFilter("clientName", value)}
        />
      )}
      <SearchFilter
        id="cases-service"
        label={t("app.cases.service")}
        placeholder={t("app.cases.service")}
        value={table.filters.serviceName}
        onChange={(value) => table.setFilter("serviceName", value)}
      />
      <FilterSelect
        id="cases-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={CASE_STATUSES.map((status) => ({ value: status, label: t(status) }))}
      />
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
        {employeeOnly ? (
          <div className="flex flex-wrap items-center gap-4">
            <span className="flex items-center gap-2">
              <Switch
                id="cases-show-all"
                checked={showAll}
                onCheckedChange={(value) => void setShowAll(value)}
              />
              <Label htmlFor="cases-show-all">{t("ShowAllPractices")}</Label>
            </span>
            <span className="flex items-center gap-2">
              <Switch
                id="cases-show-completed"
                checked={showCompleted}
                onCheckedChange={(value) => void setShowCompleted(value)}
              />
              <Label htmlFor="cases-show-completed">{t("ShowCompleted")}</Label>
            </span>
          </div>
        ) : (
          <span />
        )}
        {canOpen ? <NewCaseDialog tenant={tenant} clientId={clientId} /> : null}
      </div>
      <DataTable
        key={grid.layout ? "layout" : "default"}
        label={label}
        columns={laid.columns}
        initiallyHidden={grid.layout ? laid.hidden : ["specialization"]}
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
