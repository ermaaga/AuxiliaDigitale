"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Tabs, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";

import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect } from "@/components/data-table/filters";
import { applyLayout, type DataTableColumn } from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { tenantHref } from "@/lib/href";
import { useCan, useHasRole } from "@/lib/permissions";

import {
  REQUEST_BOXES,
  REQUEST_STATUSES,
  REQUEST_TYPES,
  REQUESTS_GRID,
  useRequests,
  type RequestBox,
  type RequestListItem,
} from "../api";
import { REQUESTS_PERMISSIONS } from "../permissions";
import { NewRequestDialog } from "./new-request-dialog";
import { RequestStatusBadge } from "./request-status-badge";

/**
 * The request inbox (F15, Q16): received (employees: addressed to them; Administrators: the office), sent, and all
 * (Administrators); filters by status and type, newest first. Clients and employees write new requests. `?open=`
 * (deep link of a notification) opens the thread.
 */
export function RequestsInbox({ tenant, label }: { tenant: string; label: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const router = useRouter();
  const isAdministrator = useHasRole("Administrator");
  const isEmployee = useHasRole("Employee");
  const isClient = useHasRole("Client");
  const canWrite = useCan(REQUESTS_PERMISSIONS.manage) && (isClient || isEmployee);
  const staff = isAdministrator || isEmployee;
  const boxes = REQUEST_BOXES.filter((box) =>
    box === "all" ? isAdministrator : box === "received" ? staff : true,
  );
  const [box, setBox] = useQueryState(
    "box",
    parseAsStringLiteral(REQUEST_BOXES)
      .withDefault(staff ? "received" : "sent")
      .withOptions({ history: "replace" }),
  );
  const [openId] = useQueryState("open");
  const table = useTableState(["status", "type"] as const);
  const grid = useGridLayout(tenant, REQUESTS_GRID);
  const current: RequestBox = boxes.includes(box) ? box : boxes[0]!;
  const query = useRequests(tenant, {
    box: current,
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[status]": table.filters.status,
    "filter[type]": table.filters.type,
  });

  React.useEffect(() => {
    if (openId) {
      router.replace(tenantHref(tenant, `/requests/${openId}`));
    }
  }, [openId, router, tenant]);

  const columns: DataTableColumn<RequestListItem>[] = [
    {
      id: "sentAt",
      header: t("CreatedAt"),
      sortField: "sentAt",
      hideable: false,
      mobile: "detail",
      cell: (row) =>
        format.dateTime(new Date(row.sentAt), { dateStyle: "medium", timeStyle: "short" }),
    },
    {
      id: "subject",
      header: t("Subject"),
      mobile: "title",
      cell: (row) => (
        <Link
          href={tenantHref(tenant, `/requests/${row.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {row.subject}
        </Link>
      ),
    },
    { id: "sender", header: t("Name"), mobile: "detail", cell: (row) => row.sender.fullName },
    {
      id: "recipient",
      header: t("app.requests.recipient"),
      cell: (row) => row.recipient?.fullName ?? t("app.requests.office"),
    },
    {
      id: "type",
      header: t("Type"),
      cell: (row) => t(`app.requests.type.${row.type}` as "app.requests.type.General"),
    },
    {
      id: "status",
      header: t("Status"),
      sortField: "status",
      mobile: "detail",
      cell: (row) => <RequestStatusBadge status={row.status} />,
    },
  ];
  const visible = columns.filter((column) => !(column.id === "sender" && current === "sent"));
  const laid = applyLayout(visible, grid.layout);

  const toolbar = (
    <>
      <FilterSelect
        id="requests-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={REQUEST_STATUSES.map((status) => ({
          value: status,
          label: t(`app.requests.status.${status}` as "app.requests.status.Pending"),
        }))}
      />
      <FilterSelect
        id="requests-type"
        label={t("Type")}
        value={table.filters.type}
        onChange={(value) => table.setFilter("type", value)}
        options={REQUEST_TYPES.map((type) => ({
          value: type,
          label: t(`app.requests.type.${type}` as "app.requests.type.General"),
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
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        {boxes.length > 1 ? (
          <Tabs value={current} onValueChange={(value) => void setBox(value as RequestBox)}>
            <TabsList aria-label={label}>
              {boxes.map((item) => (
                <TabsTrigger key={item} value={item}>
                  {t(`app.requests.box.${item}` as "app.requests.box.sent")}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        ) : (
          <span />
        )}
        {canWrite ? <NewRequestDialog tenant={tenant} /> : null}
      </div>
      <DataTable
        key={grid.layout ? "layout" : "default"}
        label={label}
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
