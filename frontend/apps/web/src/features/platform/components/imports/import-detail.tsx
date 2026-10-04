"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, CheckIcon, XIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantConsoleHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  IMPORT_ROW_STATUSES,
  cancelImport,
  confirmImport,
  deleteImport,
  isFinished,
  statusVariant,
  useImport,
  useImportEntities,
  useImportMutation,
  useImportRows,
  type ImportJob,
  type ImportRow,
} from "../../imports-api";
import { ImportProgress } from "./import-progress";
import { useEntityName } from "./tenant-imports";

/**
 * One import (F19, legacy import details): file, type, status, progress and error; once validated the rows (all or
 * one status) with the values read and the errors per column, then "Import the valid rows" or "Cancel the import";
 * a finished import can be deleted.
 */
export function ImportDetail({ slug, id }: { slug: string; id: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const entityName = useEntityName();
  const job = useImport(slug, id);
  const confirmJob = useImportMutation(slug, () => confirmImport(slug, id));
  const cancelJob = useImportMutation(slug, () => cancelImport(slug, id));
  const remove = useImportMutation(slug, () => deleteImport(slug, id));
  const busy = confirmJob.isPending || cancelJob.isPending || remove.isPending;
  const back = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantConsoleHref(slug, "/imports")}>
        <ArrowLeftIcon aria-hidden /> {t("app.platform.imports.back")}
      </Link>
    </Button>
  );

  if (job.error) {
    return (
      <div className="flex flex-col gap-4">
        {back}
        {isApiError(job.error) && job.error.status === 404 ? (
          <Alert>
            <AlertDescription>{t("app.platform.imports.notFound")}</AlertDescription>
          </Alert>
        ) : (
          <ApiErrorAlert error={job.error} onRetry={() => void job.refetch()} />
        )}
      </div>
    );
  }

  const value = job.data;
  if (!value) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true">
        {back}
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const run = async (action: () => Promise<unknown>, success: string) => {
    await action().then(
      () => notify.success(success),
      (error: unknown) => notify.error(error),
    );
  };

  const onConfirm = async () => {
    if (
      await confirm({
        title: t("app.platform.imports.confirmTitle"),
        description: t("app.platform.imports.confirmText", { count: value.successRows }),
        confirmLabel: t("app.platform.imports.confirm"),
      })
    ) {
      await run(() => confirmJob.mutateAsync(undefined), "app.platform.imports.confirmed");
    }
  };

  const onCancel = async () => {
    if (
      await confirm({
        title: t("app.platform.imports.cancelTitle"),
        description: t("app.platform.imports.cancelText"),
        confirmLabel: t("app.platform.imports.cancel"),
        variant: "destructive",
      })
    ) {
      await run(() => cancelJob.mutateAsync(undefined), "app.platform.imports.cancelled");
    }
  };

  const onDelete = async () => {
    if (
      await confirm({
        title: t("app.platform.imports.deleteTitle"),
        description: t("app.platform.imports.deleteText", { name: value.name }),
        confirmLabel: t("Delete"),
        variant: "destructive",
      })
    ) {
      await remove.mutateAsync(undefined).then(
        () => {
          notify.success("app.platform.imports.deleted");
          router.push(tenantConsoleHref(slug, "/imports"));
        },
        (error: unknown) => notify.error(error),
      );
    }
  };

  const when = (instant: string | null | undefined) =>
    instant ? format.dateTime(new Date(instant), { dateStyle: "short", timeStyle: "medium" }) : "—";

  return (
    <div className="flex flex-col gap-4">
      {back}
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="flex flex-col gap-1.5">
            <CardTitle>
              <h2 className="text-base font-semibold">{value.name}</h2>
            </CardTitle>
            <CardDescription>
              {value.fileName} · {value.importTypeName} · {entityName(value.targetEntity)}
            </CardDescription>
          </div>
          <Badge variant={statusVariant(value.status)}>
            {t(`app.platform.imports.status.${value.status}`)}
          </Badge>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <ImportProgress job={value} />
          <dl className="grid gap-2 text-sm sm:grid-cols-3">
            <div>
              <dt className="text-muted-foreground">{t("app.platform.imports.createdAt")}</dt>
              <dd>{when(value.createdAt)}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("app.platform.imports.startedAt")}</dt>
              <dd>{when(value.startedAt)}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("app.platform.imports.completedAt")}</dt>
              <dd>{when(value.completedAt)}</dd>
            </div>
          </dl>
          {value.status === "Failed" ? (
            <Alert variant="destructive">
              <AlertDescription>
                {t("app.platform.imports.failed")}{" "}
                {value.errorCode ? <code>{value.errorCode}</code> : null} {value.errorMessage}
              </AlertDescription>
            </Alert>
          ) : null}
          <div className="flex flex-wrap gap-2">
            {value.status === "AwaitingConfirmation" ? (
              <>
                <Button
                  type="button"
                  disabled={busy || Number(value.successRows) === 0}
                  onClick={() => void onConfirm()}
                >
                  <CheckIcon aria-hidden /> {t("app.platform.imports.confirm")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  disabled={busy}
                  onClick={() => void onCancel()}
                >
                  <XIcon aria-hidden /> {t("app.platform.imports.cancel")}
                </Button>
              </>
            ) : null}
            {isFinished(value.status) ? (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() => void onDelete()}
              >
                {t("Delete")}
              </Button>
            ) : null}
          </div>
        </CardContent>
      </Card>
      {value.status !== "Pending" && value.status !== "Validating" && value.status !== "Failed" ? (
        <ImportRows slug={slug} job={value} />
      ) : null}
    </div>
  );
}

function ImportRows({ slug, job }: { slug: string; job: ImportJob }) {
  const t = useTranslations();
  const entities = useImportEntities(slug);
  const [status, setStatus] = React.useState<string>();
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const rows = useImportRows(slug, job.id, status, page, pageSize, job.status !== "Cancelled");
  const fields = entities.data?.find((entity) => entity.entity === job.targetEntity)?.fields ?? [];
  const label = (key: string, fallback: string) => (t.has(key) ? t(key) : fallback);

  const columns: DataTableColumn<ImportRow>[] = [
    {
      id: "rowNumber",
      header: t("app.platform.imports.row"),
      hideable: false,
      mobile: "title",
      cell: (row) => String(row.rowNumber),
    },
    {
      id: "status",
      header: t("Status"),
      cell: (row) => (
        <Badge variant={statusVariant(row.status)}>
          {t(`app.platform.imports.rowStatus.${row.status}`)}
        </Badge>
      ),
    },
    ...fields.map((field): DataTableColumn<ImportRow> => ({
      id: `field:${field.key}`,
      header: label(field.labelKey, field.key),
      cell: (row) => {
        const errors = row.errors?.[field.key];
        return (
          <span className="flex flex-col">
            <span>{row.values[field.key] ?? "—"}</span>
            {errors?.map((key) => (
              <span key={key} className="text-xs text-destructive">
                {label(key, key)}
              </span>
            ))}
          </span>
        );
      },
    })),
    {
      id: "errors",
      header: t("app.platform.imports.rowErrors"),
      cell: (row) =>
        row.errors?.row?.map((key) => (
          <span key={key} className="block text-xs text-destructive">
            {label(key, key)}
          </span>
        )) ?? null,
    },
  ];

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1.5">
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.platform.imports.rowsTitle")}</h2>
          </CardTitle>
          <CardDescription>{t("app.platform.imports.rowsDescription")}</CardDescription>
        </div>
      </CardHeader>
      <CardContent>
        {job.status === "Cancelled" ? (
          <p className="text-sm text-muted-foreground">{t("app.platform.imports.rowsDiscarded")}</p>
        ) : (
          <DataTable
            label={t("app.platform.imports.rowsTitle")}
            columns={columns}
            rows={rows.data?.items}
            getRowId={(row) => String(row.rowNumber)}
            totalCount={Number(rows.data?.totalCount ?? 0)}
            page={page}
            pageSize={pageSize}
            sort={null}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPage(1);
            }}
            onSortChange={() => {}}
            isLoading={rows.isPending}
            error={rows.error}
            onRetry={() => void rows.refetch()}
            filtered={status !== undefined}
            toolbar={
              <FilterSelect
                id="import-row-status"
                label={t("Status")}
                value={status}
                onChange={(next) => {
                  setStatus(next);
                  setPage(1);
                }}
                options={IMPORT_ROW_STATUSES.map((item) => ({
                  value: item,
                  label: t(`app.platform.imports.rowStatus.${item}`),
                }))}
              />
            }
          />
        )}
      </CardContent>
    </Card>
  );
}
