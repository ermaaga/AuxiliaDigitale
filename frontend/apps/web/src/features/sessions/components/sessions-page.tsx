"use client";

import { LogOutIcon } from "lucide-react";
import { useFormatter, useNow, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import { SearchFilter } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";
import { ExportMenu } from "@/features/exports";

import {
  minutesBetween,
  useActiveSessions,
  useRevokeSession,
  useSessionSummary,
  type ActiveSession,
} from "../api";

/**
 * `/{tenant}/sessions` (F17, legacy `ActiveSessions` with real data, Q36): cards (open sessions, users, active in the
 * last 5 minutes), the open sessions with user, roles, client app, IP, browser, start, last use and duration,
 * refreshed every 10 s; an Administrator ends one (the user is signed out at once, `ForceLogout`).
 */
export function SessionsPage({ tenant, label }: { tenant: string; label: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const now = useNow({ updateInterval: 60_000 });
  const notify = useNotify();
  const confirm = useConfirm();
  const canRevoke = useCan("identity.sessions.revoke");
  const table = useTableState(["userName"] as const);
  const summary = useSessionSummary(tenant);
  const exportParams = {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[userName]": table.filters.userName,
  };
  const sessions = useActiveSessions(tenant, exportParams);
  const revoke = useRevokeSession(tenant);
  const date = (value: string) =>
    format.dateTime(new Date(value), { dateStyle: "short", timeStyle: "short" });

  const onRevoke = async (session: ActiveSession) => {
    if (
      await confirm({
        description: t("app.sessions.revokeConfirm", {
          name: session.fullName || session.userName,
        }),
        confirmLabel: t("app.sessions.revoke"),
        variant: "destructive",
      })
    ) {
      await revoke
        .mutateAsync(session.id)
        .then(() => notify.success("app.sessions.revoked"), notify.error);
    }
  };

  const columns: DataTableColumn<ActiveSession>[] = [
    {
      id: "user",
      header: t("app.sessions.user"),
      sortField: "userName",
      hideable: false,
      mobile: "title",
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.fullName || row.userName}</span>
          <span className="text-xs text-muted-foreground">
            {row.userName}
            {row.isCurrent ? ` · ${t("app.sessions.current")}` : ""}
          </span>
        </span>
      ),
    },
    {
      id: "roles",
      header: t("app.sessions.roles"),
      mobile: "detail",
      cell: (row) => (
        <span className="flex flex-wrap gap-1">
          {row.roles.map((role) => (
            <Badge key={role} variant="outline">
              {t.has(role) ? t(role) : role}
            </Badge>
          ))}
        </span>
      ),
    },
    { id: "client", header: t("app.sessions.client"), cell: (row) => row.clientId },
    { id: "ip", header: t("app.sessions.ip"), cell: (row) => row.ipAddress ?? "—" },
    {
      id: "userAgent",
      header: t("app.sessions.userAgent"),
      cell: (row) => (
        <span className="line-clamp-2 max-w-64 break-all text-xs">{row.userAgent ?? "—"}</span>
      ),
    },
    {
      id: "createdAt",
      header: t("app.sessions.started"),
      sortField: "createdAt",
      cell: (row) => date(row.createdAt),
    },
    {
      id: "lastUsedAt",
      header: t("app.sessions.lastUsed"),
      sortField: "lastUsedAt",
      mobile: "detail",
      cell: (row) => date(row.lastUsedAt),
    },
    {
      id: "duration",
      header: t("Duration"),
      cell: (row) => t("app.appointments.minutes", { count: minutesBetween(row.createdAt, now) }),
    },
    ...(canRevoke
      ? [
          {
            id: "actions",
            header: t("app.sessions.actions"),
            hideable: false,
            cell: (row: ActiveSession) =>
              row.isCurrent ? null : (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={revoke.isPending}
                  aria-label={t("app.sessions.revokeOf", { name: row.fullName || row.userName })}
                  onClick={() => void onRevoke(row)}
                >
                  <LogOutIcon aria-hidden /> {t("app.sessions.revoke")}
                </Button>
              ),
          } satisfies DataTableColumn<ActiveSession>,
        ]
      : []),
  ];

  const cards = [
    { key: "sessions", label: t("app.sessions.cards.sessions"), value: summary.data?.sessions },
    { key: "users", label: t("app.sessions.cards.users"), value: summary.data?.users },
    { key: "activeNow", label: t("app.sessions.cards.activeNow"), value: summary.data?.activeNow },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-3">
        {cards.map((card) => (
          <Card key={card.key}>
            <CardHeader className="pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">
                {card.label}
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-3xl font-semibold" aria-live="polite">
                {card.value === undefined ? "—" : Number(card.value)}
              </p>
            </CardContent>
          </Card>
        ))}
      </div>
      <DataTable
        label={label}
        exportMenu={(columns) => (
          <ExportMenu tenant={tenant} source="sessions" params={exportParams} columns={columns} />
        )}
        columns={columns}
        initiallyHidden={["userAgent", "client"]}
        rows={sessions.data?.items}
        getRowId={(row) => row.id}
        totalCount={Number(sessions.data?.totalCount ?? 0)}
        page={table.page}
        pageSize={table.pageSize}
        sort={table.sort}
        onPageChange={table.setPage}
        onPageSizeChange={table.setPageSize}
        onSortChange={table.setSort}
        isLoading={sessions.isPending}
        error={sessions.error}
        onRetry={() => void sessions.refetch()}
        toolbar={
          <SearchFilter
            id="sessions-user"
            label={t("Search")}
            placeholder={t("app.sessions.user")}
            value={table.filters.userName}
            onChange={(value) => table.setFilter("userName", value)}
          />
        }
        filtered={table.hasFilters}
      />
    </div>
  );
}
