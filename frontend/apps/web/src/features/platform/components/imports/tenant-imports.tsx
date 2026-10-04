"use client";

import * as React from "react";
import Link from "next/link";
import { DownloadIcon, ListIcon, PlusIcon, UploadIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
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

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantConsoleHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  MAX_IMPORT_FILE_BYTES,
  createImportType,
  deleteImport,
  deleteImportType,
  downloadTemplate,
  isFinished,
  startImport,
  statusVariant,
  useImportEntities,
  useImportMutation,
  useImportTypes,
  useImports,
  type ImportJob,
  type ImportType,
} from "../../imports-api";
import { ImportProgress } from "./import-progress";

/**
 * The imports of a tenant (F19, D-18): import types with their Excel template and columns, a new import (name, type,
 * file), then the imports with status and progress (refreshed while the Worker works) and their details.
 */
export function TenantImports({ slug }: { slug: string }) {
  return (
    <div className="flex flex-col gap-4">
      <ImportTypesCard slug={slug} />
      <NewImportCard slug={slug} />
      <ImportsCard slug={slug} />
    </div>
  );
}

/** The translated name of an import entity. */
export function useEntityName() {
  const t = useTranslations();
  return (entity: string) =>
    t.has(`app.platform.imports.entities.${entity}`)
      ? t(`app.platform.imports.entities.${entity}`)
      : entity;
}

function ImportTypesCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const entityName = useEntityName();
  const entities = useImportEntities(slug);
  const types = useImportTypes(slug);
  const [name, setName] = React.useState("");
  const [entity, setEntity] = React.useState<string>();
  const [fieldsOf, setFieldsOf] = React.useState<ImportType>();
  const create = useImportMutation(slug, () => createImportType(slug, name, entity ?? ""));
  const remove = useImportMutation(slug, (id: string) => deleteImportType(slug, id));

  const columns: DataTableColumn<ImportType>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (type) => <span className="font-medium">{type.name}</span>,
    },
    {
      id: "entity",
      header: t("app.platform.imports.entity"),
      cell: (type) => entityName(type.targetEntity),
    },
    {
      id: "createdAt",
      header: t("app.platform.imports.createdAt"),
      cell: (type) =>
        format.dateTime(new Date(type.createdAt), { dateStyle: "short", timeStyle: "short" }),
    },
    {
      id: "imports",
      header: t("app.platform.imports.count"),
      cell: (type) => String(type.importCount),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (type) => (
        <div className="flex flex-wrap gap-1">
          <Button
            variant="outline"
            size="sm"
            aria-label={t("app.platform.imports.templateNamed", { type: type.name })}
            onClick={() =>
              void downloadTemplate(slug, type).catch((error: unknown) => notify.error(error))
            }
          >
            <DownloadIcon aria-hidden /> {t("app.platform.imports.template")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            aria-label={t("app.platform.imports.fieldsNamed", { type: type.name })}
            onClick={() => setFieldsOf(type)}
          >
            <ListIcon aria-hidden /> {t("app.platform.imports.fields")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={remove.isPending || Number(type.importCount) > 0}
            aria-label={t("app.platform.imports.deleteTypeNamed", { type: type.name })}
            onClick={async () => {
              if (
                await confirm({
                  title: t("app.platform.imports.deleteTypeTitle"),
                  description: t("app.platform.imports.deleteTypeText", { type: type.name }),
                  confirmLabel: t("Delete"),
                  variant: "destructive",
                })
              ) {
                await remove.mutateAsync(type.id).then(
                  () => notify.success("app.platform.imports.typeDeleted"),
                  (error: unknown) => notify.error(error),
                );
              }
            }}
          >
            {t("Delete")}
          </Button>
        </div>
      ),
    },
  ];

  const onCreate = async (event: React.FormEvent) => {
    event.preventDefault();
    await create.mutateAsync(undefined).then(
      () => {
        setName("");
        notify.success("app.platform.imports.typeCreated");
      },
      (error: unknown) => notify.error(error),
    );
  };

  const fields =
    entities.data?.find((item) => item.entity === fieldsOf?.targetEntity)?.fields ?? [];
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.imports.typesTitle")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.imports.typesDescription")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <form className="flex flex-wrap items-end gap-2" onSubmit={(event) => void onCreate(event)}>
          <div className="flex flex-col gap-1">
            <Label htmlFor="import-type-entity">{t("app.platform.imports.entity")}</Label>
            <Select value={entity ?? ""} onValueChange={setEntity}>
              <SelectTrigger id="import-type-entity" className="w-48">
                <SelectValue placeholder={t("app.platform.imports.chooseEntity")} />
              </SelectTrigger>
              <SelectContent>
                {(entities.data ?? []).map((item) => (
                  <SelectItem key={item.entity} value={item.entity}>
                    {entityName(item.entity)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="import-type-name">{t("app.platform.imports.typeName")}</Label>
            <Input
              id="import-type-name"
              className="w-64"
              maxLength={100}
              value={name}
              placeholder={entity ? entityName(entity) : undefined}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <Button type="submit" size="sm" disabled={!entity || create.isPending}>
            <PlusIcon aria-hidden /> {t("app.platform.imports.newType")}
          </Button>
        </form>
        {entities.error ? (
          <ApiErrorAlert error={entities.error} onRetry={() => void entities.refetch()} />
        ) : null}
        <DataTable
          label={t("app.platform.imports.typesTitle")}
          columns={columns}
          rows={types.data}
          getRowId={(type) => type.id}
          totalCount={types.data?.length ?? 0}
          page={1}
          pageSize={100}
          sort={null}
          onPageChange={() => {}}
          onPageSizeChange={() => {}}
          onSortChange={() => {}}
          isLoading={types.isPending}
          error={types.error}
          onRetry={() => void types.refetch()}
          hidePaging
        />
        <Dialog
          open={fieldsOf !== undefined}
          onOpenChange={(open) => (open ? null : setFieldsOf(undefined))}
        >
          <DialogContent closeLabel={t("Close")}>
            <DialogHeader>
              <DialogTitle>
                {t("app.platform.imports.fieldsOf", { type: fieldsOf?.name ?? "" })}
              </DialogTitle>
              <DialogDescription>{t("app.platform.imports.fieldsHint")}</DialogDescription>
            </DialogHeader>
            <ul className="flex flex-col gap-2">
              {fields.map((field) => (
                <li key={field.key} className="flex flex-wrap items-center justify-between gap-2">
                  <span className="flex flex-col">
                    <span>{t.has(field.labelKey) ? t(field.labelKey) : field.labelKey}</span>
                    <code className="text-xs text-muted-foreground">{field.key}</code>
                  </span>
                  {field.required ? (
                    <Badge variant="destructive">{t("app.platform.imports.required")}</Badge>
                  ) : (
                    <Badge variant="outline">{t("app.platform.imports.optional")}</Badge>
                  )}
                </li>
              ))}
            </ul>
          </DialogContent>
        </Dialog>
      </CardContent>
    </Card>
  );
}

function NewImportCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const types = useImportTypes(slug);
  const [name, setName] = React.useState("");
  const [typeId, setTypeId] = React.useState<string>();
  const [file, setFile] = React.useState<File | null>(null);
  const fileInput = React.useRef<HTMLInputElement>(null);
  const start = useImportMutation(slug, () => startImport(slug, name.trim(), typeId ?? "", file!));
  const tooBig = file !== null && file.size > MAX_IMPORT_FILE_BYTES;

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    await start.mutateAsync(undefined).then(
      () => {
        setName("");
        setFile(null);
        if (fileInput.current) {
          fileInput.current.value = "";
        }
        notify.success("app.platform.imports.started");
      },
      (error: unknown) => notify.error(error),
    );
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.imports.newTitle")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.imports.newDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form className="flex flex-wrap items-end gap-2" onSubmit={(event) => void onSubmit(event)}>
          <div className="flex flex-col gap-1">
            <Label htmlFor="import-name">{t("app.platform.imports.name")}</Label>
            <Input
              id="import-name"
              className="w-64"
              maxLength={100}
              required
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="import-type">{t("app.platform.imports.type")}</Label>
            <Select value={typeId ?? ""} onValueChange={setTypeId}>
              <SelectTrigger id="import-type" className="w-56">
                <SelectValue placeholder={t("app.platform.imports.chooseType")} />
              </SelectTrigger>
              <SelectContent>
                {(types.data ?? []).map((type) => (
                  <SelectItem key={type.id} value={type.id}>
                    {type.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="import-file">{t("app.platform.imports.file")}</Label>
            <Input
              id="import-file"
              ref={fileInput}
              type="file"
              className="w-72"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              aria-describedby="import-file-hint"
              aria-invalid={tooBig || undefined}
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </div>
          <Button
            type="submit"
            size="sm"
            disabled={!name.trim() || !typeId || !file || tooBig || start.isPending}
          >
            <UploadIcon aria-hidden /> {t("app.platform.imports.upload")}
          </Button>
        </form>
        <p
          id="import-file-hint"
          className={
            tooBig ? "mt-2 text-sm text-destructive" : "mt-2 text-sm text-muted-foreground"
          }
        >
          {t(tooBig ? "validation.imports.fileSize" : "app.platform.imports.fileHint")}
        </p>
      </CardContent>
    </Card>
  );
}

function ImportsCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const entityName = useEntityName();
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const imports = useImports(slug, page, pageSize);
  const remove = useImportMutation(slug, (id: string) => deleteImport(slug, id));

  const columns: DataTableColumn<ImportJob>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (job) => (
        <span className="flex flex-col">
          <Link
            href={tenantConsoleHref(slug, `/imports/${job.id}`)}
            className="font-medium underline-offset-4 hover:underline"
          >
            {job.name}
          </Link>
          <span className="text-xs text-muted-foreground">{job.fileName}</span>
        </span>
      ),
    },
    {
      id: "type",
      header: t("app.platform.imports.type"),
      cell: (job) => `${job.importTypeName} · ${entityName(job.targetEntity)}`,
    },
    {
      id: "status",
      header: t("Status"),
      cell: (job) => (
        <Badge variant={statusVariant(job.status)}>
          {t(`app.platform.imports.status.${job.status}`)}
        </Badge>
      ),
    },
    {
      id: "progress",
      header: t("app.platform.imports.progress"),
      cell: (job) => <ImportProgress job={job} compact />,
    },
    {
      id: "createdAt",
      header: t("app.platform.imports.createdAt"),
      cell: (job) =>
        format.dateTime(new Date(job.createdAt), { dateStyle: "short", timeStyle: "short" }),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (job) => (
        <div className="flex flex-wrap gap-1">
          <Button variant="outline" size="sm" asChild>
            <Link
              href={tenantConsoleHref(slug, `/imports/${job.id}`)}
              aria-label={t("app.platform.imports.detailsNamed", { name: job.name })}
            >
              {t("app.platform.imports.details")}
            </Link>
          </Button>
          {isFinished(job.status) ? (
            <Button
              variant="ghost"
              size="sm"
              disabled={remove.isPending}
              aria-label={t("app.platform.imports.deleteNamed", { name: job.name })}
              onClick={async () => {
                if (
                  await confirm({
                    title: t("app.platform.imports.deleteTitle"),
                    description: t("app.platform.imports.deleteText", { name: job.name }),
                    confirmLabel: t("Delete"),
                    variant: "destructive",
                  })
                ) {
                  await remove.mutateAsync(job.id).then(
                    () => notify.success("app.platform.imports.deleted"),
                    (error: unknown) => notify.error(error),
                  );
                }
              }}
            >
              {t("Delete")}
            </Button>
          ) : null}
        </div>
      ),
    },
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.imports.listTitle")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.imports.listDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <DataTable
          label={t("app.platform.imports.listTitle")}
          columns={columns}
          rows={imports.data?.items}
          getRowId={(job) => job.id}
          totalCount={Number(imports.data?.totalCount ?? 0)}
          page={page}
          pageSize={pageSize}
          sort={null}
          onPageChange={setPage}
          onPageSizeChange={(size) => {
            setPageSize(size);
            setPage(1);
          }}
          onSortChange={() => {}}
          isLoading={imports.isPending}
          error={imports.error}
          onRetry={() => void imports.refetch()}
        />
      </CardContent>
    </Card>
  );
}
