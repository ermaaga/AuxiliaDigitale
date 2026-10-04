"use client";

import * as React from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";

import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";
import { UserAvatar } from "@/components/user-avatar";
import { ExportMenu } from "@/features/exports";
import { tenantHref } from "@/lib/href";
import { useHasRole } from "@/lib/permissions";

import { clientName, useAssignableEmployees, useClients, type ClientListItem } from "../api";
import { ClientStatusBadge } from "./client-status-badge";

/** The columns of the overview and of its PDF (legacy `ClientsOverview`, F07). */
const COLUMNS = ["lastName", "firstName", "email", "phone", "employee", "status"] as const;

/**
 * The clients overview (F07, legacy `/admin/clients-overview`): filters name, status and employee in charge (the
 * employee filter for Administrators), photo, name, e-mail, phone, employee ("not assigned") and status; rows can be
 * selected, and the export (PDF of the legacy, also CSV and Excel) takes the selected rows or every row of the
 * filters.
 */
export function ClientsOverview({ tenant, title }: { tenant: string; title: string }) {
  const t = useTranslations();
  const isAdministrator = useHasRole("Administrator");
  const table = useTableState(["fullName", "status", "employeeUserId"] as const);
  const employees = useAssignableEmployees(tenant, isAdministrator);
  const [selected, setSelected] = React.useState<ReadonlySet<string>>(new Set());
  const exportParams = {
    view: isAdministrator ? "all" : "mine",
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[fullName]": table.filters.fullName,
    "filter[status]": table.filters.status,
    "filter[employeeUserId]": isAdministrator ? table.filters.employeeUserId : undefined,
  } as const;
  const query = useClients(tenant, exportParams);

  const toggle = (id: string, on: boolean) =>
    setSelected((current) => {
      const next = new Set(current);
      if (on) {
        next.add(id);
      } else {
        next.delete(id);
      }

      return next;
    });

  const columns: DataTableColumn<ClientListItem>[] = [
    {
      id: "select",
      header: t("app.clients.overview.select"),
      hideable: false,
      cell: (row) => (
        <Checkbox
          aria-label={t("app.clients.overview.selectClient", { name: clientName(row) })}
          checked={selected.has(row.id)}
          onCheckedChange={(value) => toggle(row.id, value === true)}
        />
      ),
    },
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
    { id: "firstName", header: t("Name"), cell: (row) => row.firstName },
    { id: "email", header: t("Email"), mobile: "detail", cell: (row) => row.email ?? "—" },
    { id: "phone", header: t("Phone"), cell: (row) => row.phone ?? "—" },
    {
      id: "employee",
      header: t("app.clients.employee"),
      mobile: "detail",
      cell: (row) => row.employee?.fullName ?? t("app.clients.noEmployee"),
    },
    {
      id: "status",
      header: t("Status"),
      mobile: "detail",
      cell: (row) => <ClientStatusBadge status={row.status} />,
    },
  ];

  const toolbar = (
    <>
      <SearchFilter
        id="overview-name"
        label={t("Search")}
        placeholder={t("app.clients.overview.searchName")}
        value={table.filters.fullName}
        onChange={(value) => table.setFilter("fullName", value)}
      />
      <FilterSelect
        id="overview-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={[
          { value: "Active", label: t("Active") },
          { value: "Inactive", label: t("Inactive") },
        ]}
      />
      {isAdministrator ? (
        <FilterSelect
          id="overview-employee"
          label={t("app.clients.employee")}
          value={table.filters.employeeUserId}
          onChange={(value) => table.setFilter("employeeUserId", value)}
          options={(employees.data ?? []).map((employee) => ({
            value: employee.userId,
            label: employee.fullName,
          }))}
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
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
        <span aria-live="polite">
          {t("app.clients.overview.total", { count: Number(query.data?.totalCount ?? 0) })}
        </span>
        {selected.size > 0 ? (
          <Button type="button" variant="ghost" size="sm" onClick={() => setSelected(new Set())}>
            {t("app.clients.overview.clearSelection", { count: selected.size })}
          </Button>
        ) : null}
      </div>
      <DataTable
        label={title}
        exportMenu={() => (
          <ExportMenu
            tenant={tenant}
            source="clients"
            params={exportParams}
            columns={COLUMNS}
            ids={[...selected]}
            label={t("app.clients.overview.export")}
          />
        )}
        columns={columns}
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
    </div>
  );
}
