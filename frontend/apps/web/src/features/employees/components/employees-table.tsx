"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { MoreHorizontalIcon, PlusIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import { gridViews } from "@/components/data-table/grid-views-menu";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import { applyLayout, type DataTableColumn } from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { UserAvatar } from "@/components/user-avatar";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";
import { ExportMenu } from "@/features/exports";

import {
  deleteEmployee,
  employeeName,
  EMPLOYEES_GRID,
  makeDefaultEmployee,
  setEmployeeSignIn,
  useEmployeeMutation,
  useEmployees,
  type EmployeeListItem,
} from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";
import { EmployeeStatus } from "./employee-status";

/**
 * The employee list (F06, legacy `Admin/Employees`): picture, name, user name, e-mail, phone, status with the default
 * badge, specializations and clients in charge; filtered by name, e-mail and status, sorted and paged by the API, with
 * the columns of the role's grid layout (F21). Row actions: open, make default, enable/disable sign-in, delete.
 */
export function EmployeesTable({ tenant, title }: { tenant: string; title: string }) {
  const t = useTranslations();
  const canManage = useCan(EMPLOYEE_PERMISSIONS.manage);
  const table = useTableState(["fullName", "email", "status"] as const);
  const grid = useGridLayout(tenant, EMPLOYEES_GRID);
  const exportParams = {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[fullName]": table.filters.fullName,
    "filter[email]": table.filters.email,
    "filter[status]": table.filters.status,
  };
  const query = useEmployees(tenant, exportParams);

  const columns: DataTableColumn<EmployeeListItem>[] = [
    {
      id: "lastName",
      header: t("Surname"),
      sortField: "lastName",
      hideable: false,
      mobile: "title",
      cell: (row) => (
        <span className="flex items-center gap-2">
          <UserAvatar
            userId={row.id}
            name={employeeName(row)}
            imageVersion={row.imageVersion}
            size="sm"
          />
          <Link
            href={tenantHref(tenant, `/employees/${row.id}`)}
            className="font-medium underline-offset-4 hover:underline"
          >
            {row.lastName}
          </Link>
        </span>
      ),
    },
    { id: "firstName", header: t("Name"), sortField: "fullName", cell: (row) => row.firstName },
    { id: "userName", header: t("Username"), sortField: "userName", cell: (row) => row.userName },
    { id: "email", header: t("Email"), sortField: "email", cell: (row) => row.email ?? "—" },
    { id: "phone", header: t("Phone"), cell: (row) => row.phone ?? "—" },
    {
      id: "status",
      header: t("Status"),
      cell: (row) => <EmployeeStatus canSignIn={row.canSignIn} isDefault={row.isDefault} />,
    },
    {
      id: "specializations",
      header: t("app.employees.specializations"),
      cell: (row) =>
        row.specializations.length > 0
          ? row.specializations.map((item) => item.name).join(", ")
          : "—",
    },
    {
      id: "assignedClients",
      header: t("app.employees.assignedClients"),
      cell: (row) => Number(row.assignedClients),
    },
    ...(canManage
      ? [
          {
            id: "actions",
            header: t("app.employees.actions"),
            hideable: false,
            cell: (row: EmployeeListItem) => <RowActions tenant={tenant} employee={row} />,
          },
        ]
      : []),
  ];

  // The System's layout for the user's role (F21): order and default visibility of the columns.
  const laid = applyLayout(columns, grid.layout);

  const toolbar = (
    <>
      <SearchFilter
        id="employees-search"
        label={t("Search")}
        placeholder={t("app.clients.searchName")}
        value={table.filters.fullName}
        onChange={(value) => table.setFilter("fullName", value)}
      />
      <SearchFilter
        id="employees-email"
        label={t("Email")}
        placeholder={t("Email")}
        value={table.filters.email}
        onChange={(value) => table.setFilter("email", value)}
      />
      <FilterSelect
        id="employees-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={[
          { value: "active", label: t("Active") },
          { value: "inactive", label: t("Inactive") },
        ]}
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
      {canManage ? (
        <Button asChild className="self-end">
          <Link href={tenantHref(tenant, "/employees/new")}>
            <PlusIcon aria-hidden /> {t("CreateNewEmployee")}
          </Link>
        </Button>
      ) : null}
      <DataTable
        views={gridViews(tenant, EMPLOYEES_GRID, table)}
        // Remounted once the layout arrives, so its hidden columns become the initial column visibility.
        key={grid.layout ? "layout" : "default"}
        label={title}
        exportMenu={(columns) => (
          <ExportMenu tenant={tenant} source="employees" params={exportParams} columns={columns} />
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
    </div>
  );
}

/** The legacy grid actions of a row; the default employee cannot be disabled or deleted (`AUX-13030`). */
function RowActions({ tenant, employee }: { tenant: string; employee: EmployeeListItem }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const name = employeeName(employee);
  const makeDefault = useEmployeeMutation(tenant, () => makeDefaultEmployee(employee.id));
  const signIn = useEmployeeMutation(tenant, (on: boolean) => setEmployeeSignIn(employee.id, on));
  const remove = useEmployeeMutation(tenant, () => deleteEmployee(employee.id));

  const run = async (action: () => Promise<unknown>, success: string) => {
    try {
      await action();
      notify.success(success);
    } catch (error) {
      notify.error(error);
    }
  };

  const onDefault = async () => {
    const confirmed = await confirm({
      description: t("app.employees.makeDefaultConfirm", { name }),
      confirmLabel: t("SetDefaultEmployeeTitle"),
    });
    if (confirmed) {
      await run(() => makeDefault.mutateAsync(undefined), "app.employees.defaultSet");
    }
  };

  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.employees.deleteConfirm", { name }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (confirmed) {
      await run(() => remove.mutateAsync(undefined), "app.employees.deleted");
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          aria-label={t("app.employees.actionsOf", { name })}
        >
          <MoreHorizontalIcon aria-hidden />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem
          onSelect={() => router.push(tenantHref(tenant, `/employees/${employee.id}`))}
        >
          {t("app.employees.open")}
        </DropdownMenuItem>
        {!employee.isDefault && employee.canSignIn ? (
          <DropdownMenuItem onSelect={() => void onDefault()}>
            {t("SetDefaultEmployeeTitle")}
          </DropdownMenuItem>
        ) : null}
        {!employee.isDefault ? (
          <DropdownMenuItem
            onSelect={() =>
              void run(
                () => signIn.mutateAsync(!employee.canSignIn),
                employee.canSignIn
                  ? "app.clients.signInDisabledToast"
                  : "app.clients.signInEnabled",
              )
            }
          >
            {t(employee.canSignIn ? "app.employees.disableSignIn" : "app.employees.enableSignIn")}
          </DropdownMenuItem>
        ) : null}
        {!employee.isDefault ? (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive" onSelect={() => void onDelete()}>
              {t("Delete")}
            </DropdownMenuItem>
          </>
        ) : null}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
