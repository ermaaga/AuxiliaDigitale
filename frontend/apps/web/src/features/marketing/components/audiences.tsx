"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, PlusIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { Combobox } from "@/components/combobox";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useClients } from "@/features/clients";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  addListMembers,
  deleteList,
  removeListMembers,
  saveList,
  useListMembers,
  useMarketingMutation,
  useSegments,
  useStaticList,
  useStaticLists,
  type AudienceMember,
  type SegmentListItem,
  type StaticList,
} from "../api";
import { MARKETING_PERMISSIONS } from "../permissions";

export const LIST_NAME_MAX = 100;

/** The segments of the tenant (N01): name, description, last change; a new one opens the builder. */
export function SegmentsPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const canManage = useCan(MARKETING_PERMISSIONS.manageAudiences);
  const segments = useSegments(tenant);
  const columns: DataTableColumn<SegmentListItem>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (segment) => (
        <Link
          href={tenantHref(tenant, `/marketing/segments/${segment.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {segment.name}
        </Link>
      ),
    },
    { id: "description", header: t("Description"), cell: (segment) => segment.description ?? "—" },
    {
      id: "updatedAt",
      header: t("app.marketing.updatedAt"),
      mobile: "detail",
      cell: (segment) => format.dateTime(new Date(segment.updatedAt), { dateStyle: "medium" }),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{t("app.marketing.segments.description")}</p>
        {canManage ? (
          <Button asChild>
            <Link href={tenantHref(tenant, "/marketing/segments/new")}>
              <PlusIcon aria-hidden /> {t("app.marketing.segments.new")}
            </Link>
          </Button>
        ) : null}
      </div>
      <DataTable
        label={t("app.marketing.nav.segments")}
        columns={columns}
        rows={segments.data}
        getRowId={(segment) => segment.id}
        totalCount={segments.data?.length ?? 0}
        page={1}
        pageSize={100}
        sort={null}
        onPageChange={() => {}}
        onPageSizeChange={() => {}}
        onSortChange={() => {}}
        isLoading={segments.isPending}
        error={segments.error}
        onRetry={() => void segments.refetch()}
        hidePaging
      />
    </div>
  );
}

/** The static lists of the tenant (N01): name, members; create, open, rename and delete. */
export function ListsPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const canManage = useCan(MARKETING_PERMISSIONS.manageAudiences);
  const lists = useStaticLists(tenant);
  const [editing, setEditing] = React.useState<StaticList | "new">();
  const columns: DataTableColumn<StaticList>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (list) => (
        <Link
          href={tenantHref(tenant, `/marketing/lists/${list.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {list.name}
        </Link>
      ),
    },
    {
      id: "members",
      header: t("app.marketing.lists.members"),
      mobile: "detail",
      cell: (list) => String(list.memberCount),
    },
    { id: "description", header: t("Description"), cell: (list) => list.description ?? "—" },
    {
      id: "updatedAt",
      header: t("app.marketing.updatedAt"),
      cell: (list) => format.dateTime(new Date(list.updatedAt), { dateStyle: "medium" }),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{t("app.marketing.lists.description")}</p>
        {canManage ? (
          <Button type="button" onClick={() => setEditing("new")}>
            <PlusIcon aria-hidden /> {t("app.marketing.lists.new")}
          </Button>
        ) : null}
      </div>
      <DataTable
        label={t("app.marketing.nav.lists")}
        columns={columns}
        rows={lists.data}
        getRowId={(list) => list.id}
        totalCount={lists.data?.length ?? 0}
        page={1}
        pageSize={100}
        sort={null}
        onPageChange={() => {}}
        onPageSizeChange={() => {}}
        onSortChange={() => {}}
        isLoading={lists.isPending}
        error={lists.error}
        onRetry={() => void lists.refetch()}
        hidePaging
      />
      {editing ? (
        <ListDialog
          tenant={tenant}
          list={editing === "new" ? undefined : editing}
          onClose={() => setEditing(undefined)}
        />
      ) : null}
    </div>
  );
}

function ListDialog({
  tenant,
  list,
  onClose,
}: {
  tenant: string;
  list?: StaticList;
  onClose: () => void;
}) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const [name, setName] = React.useState(list?.name ?? "");
  const [description, setDescription] = React.useState(list?.description ?? "");
  const save = useMarketingMutation(tenant, () =>
    saveList(list?.id, name.trim(), description.trim() || null),
  );
  const nameError = isApiError(save.error) ? save.error.fieldErrors.name?.[0] : undefined;

  return (
    <Dialog open onOpenChange={(open) => (open ? null : onClose())}>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>
            {list ? t("app.marketing.lists.rename") : t("app.marketing.lists.new")}
          </DialogTitle>
          <DialogDescription>{t("app.marketing.lists.dialogDescription")}</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            void save.mutateAsync(undefined).then(
              (id) => {
                notify.success("app.marketing.lists.saved");
                onClose();
                if (!list) {
                  router.push(tenantHref(tenant, `/marketing/lists/${id}`));
                }
              },
              (error: unknown) => {
                if (!(isApiError(error) && Object.keys(error.fieldErrors).length > 0)) {
                  notify.error(error);
                }
              },
            );
          }}
        >
          <div className="flex flex-col gap-1">
            <Label htmlFor="list-name">{t("Name")} *</Label>
            <Input
              id="list-name"
              value={name}
              maxLength={LIST_NAME_MAX}
              aria-invalid={nameError ? true : undefined}
              aria-describedby={nameError ? "list-name-error" : undefined}
              onChange={(event) => setName(event.target.value)}
            />
            {nameError ? (
              <p id="list-name-error" className="text-sm text-destructive">
                {t(nameError)}
              </p>
            ) : null}
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="list-description">{t("Description")}</Label>
            <Input
              id="list-description"
              value={description}
              maxLength={500}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              {t("Cancel")}
            </Button>
            <Button type="submit" disabled={!name.trim() || save.isPending}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** A static list: its members (an employee sees the clients in their charge), add a client, remove, rename, delete. */
export function ListDetail({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(MARKETING_PERMISSIONS.manageAudiences);
  const list = useStaticList(tenant, id);
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const members = useListMembers(tenant, id, page, pageSize);
  const [renaming, setRenaming] = React.useState(false);
  const [search, setSearch] = React.useState("");
  const clients = useClients(tenant, {
    view: "all",
    page: 1,
    pageSize: 20,
    "filter[fullName]": search || undefined,
  });
  const add = useMarketingMutation(tenant, (clientId: string) => addListMembers(id, [clientId]));
  const remove = useMarketingMutation(tenant, (clientId: string) =>
    removeListMembers(id, [clientId]),
  );
  const removeList = useMarketingMutation(tenant, () => deleteList(id));

  if (list.error) {
    return <ApiErrorAlert error={list.error} onRetry={() => void list.refetch()} />;
  }

  if (!list.data) {
    return <Skeleton className="h-64 w-full" aria-label={t("Loading")} />;
  }

  const value = list.data;
  const columns: DataTableColumn<AudienceMember>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (member) => (
        <Link
          href={tenantHref(tenant, `/clients/${member.id}`)}
          className="underline-offset-4 hover:underline"
        >
          {member.fullName}
        </Link>
      ),
    },
    { id: "email", header: t("Email"), mobile: "detail", cell: (member) => member.email ?? "—" },
    { id: "status", header: t("Status"), cell: (member) => t(member.status) },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (member) =>
        canManage ? (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={remove.isPending}
            aria-label={t("app.marketing.lists.removeNamed", { name: member.fullName })}
            onClick={() =>
              void remove.mutateAsync(member.id).then(
                () => notify.success("app.marketing.lists.removed"),
                (error: unknown) => notify.error(error),
              )
            }
          >
            {t("app.marketing.lists.remove")}
          </Button>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <Button variant="ghost" size="sm" className="self-start" asChild>
        <Link href={tenantHref(tenant, "/marketing/lists")}>
          <ArrowLeftIcon aria-hidden /> {t("app.marketing.lists.back")}
        </Link>
      </Button>
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-2">
          <div className="flex flex-col gap-1.5">
            <CardTitle>
              <h2 className="text-base font-semibold">{value.name}</h2>
            </CardTitle>
            <CardDescription>
              {value.description ? `${value.description} · ` : ""}
              {t("app.marketing.lists.memberCount", { count: Number(value.memberCount) })}
            </CardDescription>
          </div>
          {canManage ? (
            <span className="flex gap-2">
              <Button type="button" variant="outline" size="sm" onClick={() => setRenaming(true)}>
                {t("Edit")}
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={removeList.isPending}
                onClick={async () => {
                  if (
                    await confirm({
                      description: t("app.marketing.lists.deleteText", { name: value.name }),
                      confirmLabel: t("Delete"),
                      variant: "destructive",
                    })
                  ) {
                    await removeList.mutateAsync(undefined).then(
                      () => {
                        notify.success("app.marketing.lists.deleted");
                        router.push(tenantHref(tenant, "/marketing/lists"));
                      },
                      (error: unknown) => notify.error(error),
                    );
                  }
                }}
              >
                {t("Delete")}
              </Button>
            </span>
          ) : null}
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {canManage ? (
            <div className="flex flex-col gap-1 sm:max-w-md">
              <Label htmlFor="list-add-client">{t("app.marketing.lists.addClient")}</Label>
              <Combobox
                id="list-add-client"
                options={(clients.data?.items ?? []).map((client) => ({
                  value: client.id,
                  label: `${client.lastName} ${client.firstName}`,
                  description: client.email ?? undefined,
                }))}
                value={undefined}
                onChange={(clientId) => {
                  if (clientId) {
                    void add.mutateAsync(clientId).then(
                      (result) =>
                        notify.success(
                          t("app.marketing.lists.added", { count: Number(result.changed) }),
                        ),
                      (error: unknown) => notify.error(error),
                    );
                  }
                }}
                placeholder={t("app.marketing.lists.chooseClient")}
                onSearch={setSearch}
                loading={clients.isFetching}
              />
              <p className="text-xs text-muted-foreground">{t("app.marketing.lists.addHint")}</p>
            </div>
          ) : null}
          <DataTable
            label={t("app.marketing.lists.members")}
            columns={columns}
            rows={members.data?.items}
            getRowId={(member) => member.id}
            totalCount={Number(members.data?.totalCount ?? 0)}
            page={page}
            pageSize={pageSize}
            sort={null}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPage(1);
            }}
            onSortChange={() => {}}
            isLoading={members.isPending}
            error={members.error}
            onRetry={() => void members.refetch()}
          />
        </CardContent>
      </Card>
      {renaming ? (
        <ListDialog tenant={tenant} list={value} onClose={() => setRenaming(false)} />
      ) : null}
    </div>
  );
}

/** Adds the selected clients to a static list (N01: lists from a selection of the clients table). */
export function AddToList({ tenant, clientIds }: { tenant: string; clientIds: readonly string[] }) {
  const t = useTranslations();
  const notify = useNotify();
  const canManage = useCan(MARKETING_PERMISSIONS.manageAudiences);
  const lists = useStaticLists(tenant, canManage);
  const [listId, setListId] = React.useState<string>();
  const add = useMarketingMutation(tenant, () => addListMembers(listId!, clientIds));
  if (!canManage || clientIds.length === 0 || (lists.data?.length ?? 0) === 0) {
    return null;
  }

  return (
    <div
      role="group"
      aria-label={t("app.marketing.lists.addSelected")}
      className="flex flex-wrap items-center gap-2"
    >
      <Select value={listId ?? ""} onValueChange={setListId}>
        <SelectTrigger className="w-44" aria-label={t("app.marketing.lists.chooseList")}>
          <SelectValue placeholder={t("app.marketing.lists.chooseList")} />
        </SelectTrigger>
        <SelectContent>
          {(lists.data ?? []).map((list) => (
            <SelectItem key={list.id} value={list.id}>
              {list.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        type="button"
        size="sm"
        variant="outline"
        disabled={!listId || add.isPending}
        onClick={() =>
          void add.mutateAsync(undefined).then(
            (result) =>
              notify.success(t("app.marketing.lists.added", { count: Number(result.changed) })),
            (error: unknown) => notify.error(error),
          )
        }
      >
        {t("app.marketing.lists.addSelectedCount", { count: clientIds.length })}
      </Button>
    </div>
  );
}
