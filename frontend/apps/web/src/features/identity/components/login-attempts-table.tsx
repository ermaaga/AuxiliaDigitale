"use client";

import * as React from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";
import { Badge } from "@auxilia/ui/components/badge";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Button } from "@auxilia/ui/components/button";
import { useFormatter, useTranslations } from "next-intl";

import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";
import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

type LoginAttempt = components["schemas"]["LoginAttemptResponse"];

const ALL = "all";

/**
 * Login audit (F35, `GET /identity/login-attempts`, permission `identity.loginAttempts.view`): every sign-in attempt,
 * filtered by user name, method and result, sorted by date or user, paged by the API; the state lives in the URL.
 */
export function LoginAttemptsTable({ tenant, title }: { tenant: string; title: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const table = useTableState(["userName", "method", "succeeded"] as const);
  const [search, setSearch] = React.useState(table.filters.userName ?? "");

  // Debounced search: the URL (and the query) change 300 ms after the last keystroke.
  React.useEffect(() => {
    const current = table.filters.userName ?? "";
    if (search === current) {
      return;
    }

    const timer = window.setTimeout(
      () => table.setFilter("userName", search.trim() || undefined),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [search, table]);

  const params = {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[userName]": table.filters.userName,
    "filter[method]": table.filters.method,
    "filter[succeeded]":
      table.filters.succeeded === undefined ? undefined : table.filters.succeeded === "true",
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

  const toolbar = (
    <>
      <div className="flex min-w-48 flex-1 flex-col gap-1 sm:max-w-64">
        <Label htmlFor="attempts-search">{t("Search")}</Label>
        <Input
          id="attempts-search"
          type="search"
          placeholder={t("app.identity.loginAttempts.searchUser")}
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </div>
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
      {table.hasFilters ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => {
            setSearch("");
            table.clearFilters();
          }}
        >
          {t("ClearFilters")}
        </Button>
      ) : null}
    </>
  );

  return (
    <DataTable
      label={title}
      columns={columns}
      initiallyHidden={["userAgent"]}
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
  );
}

function FilterSelect({
  id,
  label,
  value,
  onChange,
  options,
}: {
  id: string;
  label: string;
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  options: readonly { value: string; label: string }[];
}) {
  const t = useTranslations();
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={id}>{label}</Label>
      <Select
        value={value ?? ALL}
        onValueChange={(next) => onChange(next === ALL ? undefined : next)}
      >
        <SelectTrigger id={id} className="w-40">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL}>{t("common.table.all")}</SelectItem>
          {options.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
