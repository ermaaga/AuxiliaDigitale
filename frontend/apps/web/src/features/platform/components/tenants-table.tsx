"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";

import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import { pageLocally, type DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";
import { tenantConsoleHref } from "@/lib/href";

import type { PlatformTenant } from "../server";
import { TENANT_STATUSES, statusLabel } from "../tenant-status";

const STATUS_VARIANT: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Active: "secondary",
  Provisioning: "outline",
  Suspended: "destructive",
  MigrationFailed: "destructive",
  Archived: "outline",
};

/** Tenants whose name or slug contains the search text, with the given status. */
export function filterTenants(
  tenants: readonly PlatformTenant[],
  search: string | undefined,
  status: string | undefined,
): PlatformTenant[] {
  const text = search?.trim().toLocaleLowerCase();
  return tenants.filter(
    (tenant) =>
      (!status || tenant.status === status) &&
      (!text ||
        tenant.slug.includes(text) ||
        tenant.displayName.toLocaleLowerCase().includes(text)),
  );
}

/**
 * Tenant list of the console (N02): name, identifier, status and schema version; search, status filter, sort and
 * paging in the URL, done in the browser (`GET /platform/tenants` returns every tenant). The name opens the tenant.
 */
export function TenantsTable({
  tenants,
  title,
}: {
  tenants: readonly PlatformTenant[];
  title: string;
}) {
  const t = useTranslations();
  const table = useTableState(["search", "status"] as const);
  const filtered = filterTenants(tenants, table.filters.search, table.filters.status);
  const { rows, totalCount, page } = pageLocally(filtered, {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort,
    sortValue: (tenant, field) =>
      field === "slug"
        ? tenant.slug
        : field === "status"
          ? tenant.status
          : field === "schemaVersion"
            ? (tenant.schemaVersion ?? "")
            : tenant.displayName,
  });

  const columns: DataTableColumn<PlatformTenant>[] = [
    {
      id: "displayName",
      header: t("Name"),
      sortField: "displayName",
      hideable: false,
      mobile: "title",
      cell: (tenant) => (
        <Link
          href={tenantConsoleHref(tenant.slug)}
          className="font-medium text-primary-text underline-offset-4 hover:underline"
        >
          {tenant.displayName}
        </Link>
      ),
    },
    {
      id: "slug",
      header: t("app.platform.tenants.slug"),
      sortField: "slug",
      cell: (tenant) => <code className="text-sm">{tenant.slug}</code>,
    },
    {
      id: "status",
      header: t("Status"),
      sortField: "status",
      cell: (tenant) => (
        <Badge variant={STATUS_VARIANT[tenant.status] ?? "outline"}>
          {statusLabel(t, tenant.status)}
        </Badge>
      ),
    },
    {
      id: "schemaVersion",
      header: t("app.platform.tenants.schemaVersion"),
      sortField: "schemaVersion",
      cell: (tenant) => (
        <span className="text-sm text-muted-foreground">{tenant.schemaVersion ?? "—"}</span>
      ),
    },
  ];

  const toolbar = (
    <>
      <SearchFilter
        id="tenants-search"
        label={t("Search")}
        placeholder={t("app.platform.tenants.search")}
        value={table.filters.search}
        onChange={(value) => table.setFilter("search", value)}
      />
      <FilterSelect
        id="tenants-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={TENANT_STATUSES.map((status) => ({
          value: status,
          label: statusLabel(t, status),
        }))}
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
      label={title}
      columns={columns}
      rows={rows}
      getRowId={(tenant) => tenant.slug}
      totalCount={totalCount}
      page={page}
      pageSize={table.pageSize}
      sort={table.sort}
      onPageChange={table.setPage}
      onPageSizeChange={table.setPageSize}
      onSortChange={table.setSort}
      toolbar={toolbar}
      filtered={table.hasFilters}
    />
  );
}
