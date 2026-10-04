"use client";

import * as React from "react";
import { useQueryState } from "nuqs";
import { DownloadIcon } from "lucide-react";
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
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import { download, exportFileUrl, useMyExports, type ExportJob } from "../api";

/**
 * `/{tenant}/exports` (F26): the user's large exports of the last 24 hours (queued, ready, failed) with their files;
 * `?download={id}` (the link of the `export.ready` notification) downloads that one at once.
 */
export function ExportsPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const exports = useMyExports(tenant);
  const [requested, setRequested] = useQueryState("download", { history: "replace" });
  const started = React.useRef<string | null>(null);

  const get = React.useCallback(
    async (job: Pick<ExportJob, "id" | "fileName">) => {
      try {
        await download(exportFileUrl(job.id), job.fileName ?? "export");
      } catch (error) {
        notify.error(error);
      }
    },
    [notify],
  );

  React.useEffect(() => {
    const job = exports.data?.find((item) => item.id === requested);
    if (requested && job?.status === "Ready" && started.current !== requested) {
      started.current = requested;
      void get(job).finally(() => void setRequested(null));
    }
  }, [exports.data, requested, get, setRequested]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("app.exports.title")}</CardTitle>
        <CardDescription>{t("app.exports.description")}</CardDescription>
      </CardHeader>
      <CardContent>
        {exports.error ? (
          <ApiErrorAlert error={exports.error} onRetry={() => void exports.refetch()} />
        ) : null}
        {exports.isPending ? (
          <Skeleton className="h-24 w-full" />
        ) : (exports.data ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.exports.none")}</p>
        ) : (
          <ul className="flex flex-col divide-y" aria-label={t("app.exports.title")}>
            {(exports.data ?? []).map((job) => (
              <li key={job.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span className="flex min-w-0 flex-col">
                  <span className="truncate font-medium">{job.fileName ?? job.source}</span>
                  <span className="text-xs text-muted-foreground">
                    {format.dateTime(new Date(job.createdAt), {
                      dateStyle: "medium",
                      timeStyle: "short",
                    })}
                    {job.rowCount
                      ? ` · ${t("app.exports.rows", { count: Number(job.rowCount) })}`
                      : ""}
                  </span>
                </span>
                <span className="flex items-center gap-2">
                  <Badge
                    variant={
                      job.status === "Ready"
                        ? "secondary"
                        : job.status === "Failed"
                          ? "destructive"
                          : "outline"
                    }
                  >
                    {t(`app.exports.status.${job.status}` as "app.exports.status.Ready")}
                  </Badge>
                  {job.status === "Ready" ? (
                    <Button type="button" variant="outline" size="sm" onClick={() => void get(job)}>
                      <DownloadIcon aria-hidden /> {t("app.exports.download")}
                    </Button>
                  ) : null}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
