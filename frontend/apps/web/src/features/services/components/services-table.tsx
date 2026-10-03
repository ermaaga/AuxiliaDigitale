"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { PlusIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@auxilia/ui/components/dialog";

import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import { applyLayout, type DataTableColumn } from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  createService,
  SERVICES_GRID,
  useServiceCategories,
  useServiceMutation,
  useServices,
  type Service,
} from "../api";
import { SERVICES_PERMISSIONS } from "../permissions";
import { serviceBody, type ServiceOutput } from "../schemas/service";
import { CategoriesDialog } from "./categories-dialog";
import { ServiceForm } from "./service-form";

/** Money in euro in the page language. */
export const euro = (amount: number | string, currency: string, locale: string) =>
  new Intl.NumberFormat(locale, { style: "currency", currency }).format(Number(amount));

/**
 * The service catalog (F08, legacy `Memberships`): filters by name, category and status, sorted by name, price or
 * duration (default name), the grid layout of the role (F21); a new service in a dialog and the categories (Q26).
 */
export function ServicesTable({ tenant, label }: { tenant: string; label: string }) {
  const t = useTranslations();
  const locale = useLocale();
  const router = useRouter();
  const notify = useNotify();
  const canManage = useCan(SERVICES_PERMISSIONS.manage);
  const [open, setOpen] = React.useState(false);
  const table = useTableState(["name", "categoryId", "active"] as const);
  const grid = useGridLayout(tenant, SERVICES_GRID);
  const categories = useServiceCategories(tenant);
  const create = useServiceMutation(tenant, (values: ServiceOutput) =>
    createService(serviceBody(values)),
  );
  const query = useServices(tenant, {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[name]": table.filters.name,
    "filter[categoryId]": table.filters.categoryId,
    "filter[active]":
      table.filters.active === undefined ? undefined : table.filters.active === "true",
  });

  const columns: DataTableColumn<Service>[] = [
    {
      id: "name",
      header: t("Name"),
      sortField: "name",
      hideable: false,
      mobile: "title",
      cell: (row) => (
        <Link
          href={tenantHref(tenant, `/services/${row.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {row.name}
        </Link>
      ),
    },
    { id: "description", header: t("Description"), cell: (row) => row.description ?? "—" },
    {
      id: "category",
      header: t("app.services.category"),
      cell: (row) => row.category?.name ?? "—",
    },
    {
      id: "specialization",
      header: t("Specialization"),
      cell: (row) => row.specialization?.name ?? "—",
    },
    {
      id: "price",
      header: t("Price"),
      sortField: "price",
      mobile: "detail",
      cell: (row) => euro(row.price, row.currency, locale),
    },
    {
      id: "durationDays",
      header: t("DurationDays"),
      sortField: "durationDays",
      cell: (row) => Number(row.durationDays),
    },
    {
      id: "status",
      header: t("Status"),
      cell: (row) =>
        row.isActive ? (
          <Badge variant="secondary">{t("Active")}</Badge>
        ) : (
          <Badge variant="outline">{t("Inactive")}</Badge>
        ),
    },
  ];
  const laid = applyLayout(columns, grid.layout);

  const toolbar = (
    <>
      <SearchFilter
        id="services-name"
        label={t("Search")}
        placeholder={t("Name")}
        value={table.filters.name}
        onChange={(value) => table.setFilter("name", value)}
      />
      <FilterSelect
        id="services-category"
        label={t("app.services.category")}
        value={table.filters.categoryId}
        onChange={(value) => table.setFilter("categoryId", value)}
        options={(categories.data ?? []).map((category) => ({
          value: category.id,
          label: category.name,
        }))}
      />
      <FilterSelect
        id="services-status"
        label={t("Status")}
        value={table.filters.active}
        onChange={(value) => table.setFilter("active", value)}
        options={[
          { value: "true", label: t("Active") },
          { value: "false", label: t("Inactive") },
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
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Dialog open={open} onOpenChange={setOpen}>
            <DialogTrigger asChild>
              <Button type="button">
                <PlusIcon aria-hidden /> {t("app.services.new")}
              </Button>
            </DialogTrigger>
            <DialogContent closeLabel={t("Close")}>
              <DialogHeader>
                <DialogTitle>{t("app.services.new")}</DialogTitle>
                <DialogDescription>{t("app.services.newDescription")}</DialogDescription>
              </DialogHeader>
              <ServiceForm
                tenant={tenant}
                submitLabel={t("Create")}
                error={create.error}
                onSubmit={async (values) => {
                  const created = await create.mutateAsync(values);
                  notify.success("app.services.created");
                  setOpen(false);
                  router.push(tenantHref(tenant, `/services/${created.id}`));
                }}
              />
            </DialogContent>
          </Dialog>
          <CategoriesDialog tenant={tenant} />
        </div>
      ) : null}
      <DataTable
        key={grid.layout ? "layout" : "default"}
        label={label}
        columns={laid.columns}
        initiallyHidden={grid.layout ? laid.hidden : ["description"]}
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
