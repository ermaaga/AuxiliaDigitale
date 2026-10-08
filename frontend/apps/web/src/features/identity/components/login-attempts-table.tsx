"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { useFormatter, useTranslations } from "next-intl";

import { DataTable } from "@/components/data-table/data-table";
import { gridViews } from "@/components/data-table/grid-views-menu";
import {
  DateFilter,
  dayBoundary,
  FilterSelect,
  SearchFilter,
} from "@/components/data-table/filters";
import { applyLayout, type DataTableColumn } from "@/components/data-table/table-model";
import { useGridLayout } from "@/components/data-table/use-grid-layout";
import { useTableState } from "@/components/data-table/use-table-state";
import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";
import { ExportMenu } from "@/features/exports";

type LoginAttempt = components["schemas"]["LoginAttemptResponse"];

/**
 * Login audit (F35, `GET /identity/login-attempts`, permission `identity.loginAttempts.view`): every sign-in attempt,
 * filtered by user name, method, result and day range, sorted by date or user, paged by the API; the state lives in the URL. The
 * columns follow the grid layout `identity.loginAttempts` of the user's role (F21).
 */
export function LoginAttemptsTable({ tenant, title }: { tenant: string; title: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const table = useTableState(["userName", "method", "succeeded", "from", "to"] as const);
  const grid = useGridLayout(tenant, "identity.loginAttempts");
  const params = {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[userName]": table.filters.userName,
    "filter[method]": table.filters.method,
    "filter[succeeded]":
      table.filters.succeeded === undefined ? undefined : table.filters.succeeded === "true",
    // Days of the browser's calendar, both included (the API takes instants).
    "filter[from]": dayBoundary(table.filters.from, "from"),
    "filter[to]": dayBoundary(table.filters.to, "to"),
  };

  const query = useQuery({
    queryKey: queryKey(tenant, "identity", "login-attempts", params),
    queryFn: async () =>
      unwrap(
        await createBffClient("tenant").GET("/api/v1/identity/login-attempts", {
          params: { query: params },
        }),
      ),
    placeholderData: keepPreviousData,
  });

  const method = (code: string) =>
    t.has(`app.identity.loginMethods.${code}`) ? t(`app.identity.loginMethods.${code}`) : code;
  const reason = (code: string) =>
    t.has(`app.identity.loginFailures.${code}`) ? t(`app.identity.loginFailures.${code}`) : code;

  const columns: DataTableColumn<LoginAttempt>[] = [
    {
      id: "attemptedAt",
      header: t("Date"),
      sortField: "attemptedAt",
      hideable: false,
      mobile: "title",
      cell: (row) =>
        format.dateTime(new Date(row.attemptedAt), { dateStyle: "medium", timeStyle: "short" }),
    },
    { id: "userName", header: t("Username"), sortField: "userName", cell: (row) => row.userName },
    { id: "method", header: t("LoginType"), cell: (row) => method(row.method) },
    {
      id: "result",
      header: t("LoginResult"),
      cell: (row) =>
        row.succeeded ? (
          <Badge variant="secondary">{t("app.identity.loginAttempts.succeeded")}</Badge>
        ) : (
          <Badge variant="destructive">{t("Failed")}</Badge>
        ),
    },
    {
      id: "failureReason",
      header: t("FailureReason"),
      cell: (row) => (row.failureReason ? reason(row.failureReason) : "—"),
    },
    {
      id: "ipAddress",
      header: t("app.identity.loginAttempts.ipAddress"),
      cell: (row) => row.ipAddress ?? "—",
    },
    {
      id: "userAgent",
      header: t("app.identity.loginAttempts.userAgent"),
      mobile: "hidden",
      cell: (row) => (
        <span
          className="line-clamp-1 max-w-64 text-muted-foreground"
          title={row.userAgent ?? undefined}
        >
          {row.userAgent ?? "—"}
        </span>
      ),
    },
  ];

  // The System's layout for the user's role (F21): order and default visibility of the columns.
  const laid = applyLayout(columns, grid.layout);

  const toolbar = (
    <>
      <SearchFilter
        id="attempts-search"
        label={t("Search")}
        placeholder={t("app.identity.loginAttempts.searchUser")}
        value={table.filters.userName}
        onChange={(value) => table.setFilter("userName", value)}
      />
      <FilterSelect
        id="attempts-method"
        label={t("LoginType")}
        value={table.filters.method}
        onChange={(value) => table.setFilter("method", value)}
        options={[
          { value: "password", label: method("password") },
          { value: "email-otp", label: method("email-otp") },
        ]}
      />
      <FilterSelect
        id="attempts-result"
        label={t("LoginResult")}
        value={table.filters.succeeded}
        onChange={(value) => table.setFilter("succeeded", value)}
        options={[
          { value: "true", label: t("app.identity.loginAttempts.succeeded") },
          { value: "false", label: t("Failed") },
        ]}
      />
      <DateFilter
        id="attempts-from"
        label={t("app.identity.loginAttempts.from")}
        value={table.filters.from}
        onChange={(value) => table.setFilter("from", value)}
      />
      <DateFilter
        id="attempts-to"
        label={t("app.identity.loginAttempts.to")}
        value={table.filters.to}
        onChange={(value) => table.setFilter("to", value)}
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
      views={gridViews(tenant, "identity.loginAttempts", table)}
      // Remounted once the layout arrives, so its hidden columns become the initial column visibility.
      key={grid.layout ? "layout" : "default"}
      label={title}
      exportMenu={(columns) => (
        <ExportMenu tenant={tenant} source="login-attempts" params={params} columns={columns} />
      )}
      columns={laid.columns}
      initiallyHidden={grid.layout ? laid.hidden : ["userAgent"]}
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
