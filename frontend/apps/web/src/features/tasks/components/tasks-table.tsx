"use client";

import * as React from "react";
import Link from "next/link";
import { CheckIcon, PlusIcon, Undo2Icon } from "lucide-react";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  TASK_SCOPES,
  TASK_STATUSES,
  completeTask,
  deleteTask,
  reopenTask,
  useTask,
  useTaskMutation,
  useTasks,
  type TaskItem,
} from "../api";
import { TASKS_PERMISSIONS } from "../permissions";
import { TaskDialog, type TaskContext } from "./task-dialog";

/**
 * Tasks (B-26): mine or all I may see, by status and "due today" (open tasks due today or before, overdue in red),
 * sorted by due date; new task, edit, done / open again, delete. From a client the list shows its tasks only and a new
 * task is about it. `?open={id}` (link of the notification) opens the task.
 */
export function TasksTable({ tenant, context }: { tenant: string; context?: TaskContext }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(TASKS_PERMISSIONS.manage);
  const [scope, setScope] = useQueryState(
    "scope",
    parseAsStringLiteral(TASK_SCOPES)
      .withDefault(context ? "all" : "mine")
      .withOptions({ history: "replace" }),
  );
  const [openId, setOpenId] = useQueryState("open");
  const table = useTableState(["status", "due"] as const);
  const [creating, setCreating] = React.useState(false);
  const [editing, setEditing] = React.useState<TaskItem>();
  const opened = useTask(tenant, openId ?? undefined);
  const query = useTasks(tenant, {
    scope,
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    "filter[status]": table.filters.status,
    "filter[due]": table.filters.due,
    "filter[clientId]": context?.clientId,
    "filter[caseId]": context?.caseId,
  });
  const complete = useTaskMutation(tenant, (id: string) => completeTask(id));
  const reopen = useTaskMutation(tenant, (id: string) => reopenTask(id));
  const remove = useTaskMutation(tenant, (id: string) => deleteTask(id));
  const busy = complete.isPending || reopen.isPending || remove.isPending;
  const current = editing ?? opened.data;

  const columns: DataTableColumn<TaskItem>[] = [
    {
      id: "title",
      header: t("app.tasks.title"),
      sortField: "title",
      hideable: false,
      mobile: "title",
      cell: (task) => (
        <span className="flex flex-col">
          <span className={task.status === "Done" ? "font-medium line-through" : "font-medium"}>
            {task.title}
          </span>
          {task.notes ? (
            <span className="line-clamp-2 text-xs text-muted-foreground">{task.notes}</span>
          ) : null}
        </span>
      ),
    },
    {
      id: "dueOn",
      header: t("app.tasks.dueOn"),
      sortField: "dueOn",
      mobile: "detail",
      cell: (task) =>
        task.dueOn ? (
          <span className="flex flex-wrap items-center gap-1">
            {format.dateTime(new Date(task.dueOn), { dateStyle: "medium", timeZone: "UTC" })}
            {task.isOverdue ? <Badge variant="destructive">{t("app.tasks.overdue")}</Badge> : null}
          </span>
        ) : (
          "—"
        ),
    },
    {
      id: "assignee",
      header: t("app.tasks.assignee"),
      mobile: "detail",
      cell: (task) => task.assignee.fullName,
    },
    {
      id: "client",
      header: t("Client"),
      cell: (task) =>
        task.client ? (
          <span className="flex flex-col">
            <Link
              href={tenantHref(tenant, `/clients/${task.client.id}`)}
              className="underline-offset-4 hover:underline"
            >
              {task.client.fullName}
            </Link>
            {task.case ? (
              <Link
                href={tenantHref(tenant, `/cases/${task.case.id}`)}
                className="text-xs text-muted-foreground underline-offset-4 hover:underline"
              >
                {task.case.number}
              </Link>
            ) : null}
          </span>
        ) : (
          "—"
        ),
    },
    {
      id: "status",
      header: t("Status"),
      mobile: "detail",
      cell: (task) => (
        <Badge variant={task.status === "Done" ? "secondary" : "outline"}>
          {t(`app.tasks.status.${task.status}`)}
        </Badge>
      ),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (task) =>
        canManage ? (
          <div className="flex flex-wrap gap-1">
            {task.status === "Open" ? (
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                aria-label={t("app.tasks.completeNamed", { title: task.title })}
                onClick={() =>
                  void complete.mutateAsync(task.id).then(
                    () => notify.success("app.tasks.completed"),
                    (error: unknown) => notify.error(error),
                  )
                }
              >
                <CheckIcon aria-hidden /> {t("app.tasks.complete")}
              </Button>
            ) : (
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                aria-label={t("app.tasks.reopenNamed", { title: task.title })}
                onClick={() =>
                  void reopen.mutateAsync(task.id).then(
                    () => notify.success("app.tasks.reopened"),
                    (error: unknown) => notify.error(error),
                  )
                }
              >
                <Undo2Icon aria-hidden /> {t("app.tasks.reopen")}
              </Button>
            )}
            <Button
              variant="ghost"
              size="sm"
              aria-label={t("app.tasks.editNamed", { title: task.title })}
              onClick={() => setEditing(task)}
            >
              {t("Edit")}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              disabled={busy}
              aria-label={t("app.tasks.deleteNamed", { title: task.title })}
              onClick={async () => {
                if (
                  await confirm({
                    title: t("app.tasks.deleteTitle"),
                    description: t("app.tasks.deleteText", { title: task.title }),
                    confirmLabel: t("Delete"),
                    variant: "destructive",
                  })
                ) {
                  await remove.mutateAsync(task.id).then(
                    () => notify.success("app.tasks.deleted"),
                    (error: unknown) => notify.error(error),
                  );
                }
              }}
            >
              {t("Delete")}
            </Button>
          </div>
        ) : null,
    },
  ];
  const visible = context ? columns.filter((column) => column.id !== "client") : columns;

  const toolbar = (
    <>
      <FilterSelect
        id="tasks-status"
        label={t("Status")}
        value={table.filters.status}
        onChange={(value) => table.setFilter("status", value)}
        options={TASK_STATUSES.map((status) => ({
          value: status,
          label: t(`app.tasks.status.${status}`),
        }))}
      />
      <FilterSelect
        id="tasks-due"
        label={t("app.tasks.due")}
        value={table.filters.due}
        onChange={(value) => table.setFilter("due", value)}
        options={[{ value: "today", label: t("app.tasks.dueToday") }]}
      />
      {table.hasFilters ? (
        <Button type="button" variant="ghost" size="sm" onClick={table.clearFilters}>
          {t("ClearFilters")}
        </Button>
      ) : null}
    </>
  );

  // The scope tabs control the table: it is the panel of the selected tab (aria-controls points at it).
  return (
    <Tabs
      value={scope}
      onValueChange={(value) => void setScope(value as (typeof TASK_SCOPES)[number])}
      className="gap-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <TabsList aria-label={t("app.tasks.scope")}>
          {TASK_SCOPES.map((item) => (
            <TabsTrigger key={item} value={item}>
              {t(`app.tasks.scopes.${item}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        {canManage ? (
          <Button type="button" onClick={() => setCreating(true)}>
            <PlusIcon aria-hidden /> {t("app.tasks.new")}
          </Button>
        ) : null}
      </div>
      <TabsContent value={scope}>
        <DataTable
          label={t("nav.tasks")}
          columns={visible}
          rows={query.data?.items}
          getRowId={(task) => task.id}
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
      </TabsContent>
      {creating ? (
        <TaskDialog tenant={tenant} context={context} open onOpenChange={setCreating} />
      ) : null}
      {current && canManage ? (
        <TaskDialog
          key={current.id}
          tenant={tenant}
          task={current}
          open
          onOpenChange={(open) => {
            if (!open) {
              setEditing(undefined);
              void setOpenId(null);
            }
          }}
        />
      ) : null}
    </Tabs>
  );
}
