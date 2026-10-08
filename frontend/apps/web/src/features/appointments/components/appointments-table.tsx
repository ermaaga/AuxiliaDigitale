"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";

import { DataTable } from "@/components/data-table/data-table";
import { gridViews } from "@/components/data-table/grid-views-menu";
import { FilterSelect } from "@/components/data-table/filters";
import { applyLayout, type DataTableColumn } from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { useHasRole } from "@/lib/permissions";
import { ExportMenu } from "@/features/exports";

import {
  APPOINTMENT_STATUSES,
  APPOINTMENTS_GRID,
  useAppointments,
  type AppointmentListItem,
} from "../api";
import { shortTime } from "../schemas/appointment";
import { AppointmentStatusBadge } from "./appointment-status-badge";

/**
 * The appointment grid (F13, legacy grids `Appointments/Employee` and client): client, operator, date and time,
 * duration, global calendar, status; latest first, sorted by date, status, client or operator; a row opens the
 * drawer. `clientId` keeps one client (client 360°).
 */
export function AppointmentsTable({
  tenant,
  label,
  clientId,
  onOpen,
}: {
  tenant: string;
  label: string;
  clientId?: string;
  onOpen: (id: string) => void;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const isClient = useHasRole("Client");
  const isAdministrator = useHasRole("Administrator");
  const isEmployee = useHasRole("Employee");
  const staff = isAdministrator || isEmployee;
  const table = useTableState(["status"] as const);
  const grid = useGridLayout(tenant, APPOINTMENTS_GRID);
  const exportParams = {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[clientId]": clientId,
    "filter[status]": table.filters.status,
  };
  const query = useAppointments(tenant, exportParams);

  const columns: DataTableColumn<AppointmentListItem>[] = [
    {
      id: "startsAt",
      header: t("DateTime"),
      sortField: "startsAt",
      hideable: false,
      mobile: "title",
      cell: (row) => (
        <button
          type="button"
          className="text-left font-medium underline-offset-4 hover:underline"
          onClick={() => onOpen(row.id)}
        >
          {format.dateTime(new Date(`${row.date}T${row.time}`), { dateStyle: "medium" })}{" "}
          {shortTime(row.time)}
        </button>
      ),
    },
    {
      id: "client",
      header: t("Client"),
      sortField: "client",
      mobile: "detail",
      cell: (row) => row.client.fullName,
    },
    {
      id: "employee",
      header: t("app.appointments.operator"),
      sortField: "employee",
      mobile: "detail",
      cell: (row) => row.employee.fullName,
    },
    {
      id: "duration",
      header: t("DurationMinutes"),
      cell: (row) => t("app.appointments.minutes", { count: row.durationMinutes }),
    },
    {
      id: "showInGlobalCalendar",
      header: t("ShowInGlobalCalendar"),
      cell: (row) => (row.showInGlobalCalendar ? t("Yes") : t("No")),
    },
    {
      id: "status",
      header: t("Status"),
      sortField: "status",
      mobile: "detail",
      cell: (row) => <AppointmentStatusBadge status={row.status} />,
    },
  ];
  const visible = columns.filter(
    (column) =>
      !(column.id === "client" && (isClient || clientId)) &&
      !(column.id === "showInGlobalCalendar" && !staff),
  );
  const laid = applyLayout(visible, grid.layout);

  const toolbar = (
    <>
      <FilterSelect
        id="appointments-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={APPOINTMENT_STATUSES.map((status) => ({ value: status, label: t(status) }))}
      />
      {table.hasFilters ? (
        <Button type="button" variant="ghost" size="sm" onClick={table.clearFilters}>
          {t("ClearFilters")}
        </Button>
      ) : null}
    </>
  );

  return (
    <DataTable
      views={gridViews(tenant, APPOINTMENTS_GRID, table)}
      key={grid.layout ? "layout" : "default"}
      label={label}
      exportMenu={(columns) => (
        <ExportMenu tenant={tenant} source="appointments" params={exportParams} columns={columns} />
      )}
      columns={laid.columns}
      initiallyHidden={grid.layout ? laid.hidden : []}
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
  );
}
