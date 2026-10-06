"use client";

import * as React from "react";
import { PlayIcon } from "lucide-react";
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@auxilia/ui/components/table";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import { jobKey, useJobRuns, useJobs, useRunJob, type Job, type JobRun } from "../../jobs-api";

/** How long the page keeps refreshing after "run now": the Worker picks the message up within seconds. */
const REFRESH_AFTER_REQUEST_MS = 60_000;

/**
 * The recurring jobs of a tenant (N02, D-15: never scheduled): what each one does, its last run (who, when, result)
 * and "run now", which the Worker carries out; below, the latest runs of every job.
 */
export function TenantJobs({ slug }: { slug: string }) {
  const [refreshUntil, setRefreshUntil] = React.useState(0);
  const refreshing = refreshUntil > 0;
  React.useEffect(() => {
    if (!refreshUntil) {
      return;
    }

    const timer = window.setTimeout(() => setRefreshUntil(0), refreshUntil - Date.now());
    return () => window.clearTimeout(timer);
  }, [refreshUntil]);

  return (
    <div className="flex flex-col gap-4">
      <JobsCard
        slug={slug}
        refreshing={refreshing}
        onRequested={() => setRefreshUntil(Date.now() + REFRESH_AFTER_REQUEST_MS)}
      />
      <RunsCard slug={slug} refreshing={refreshing} />
    </div>
  );
}

function JobsCard({
  slug,
  refreshing,
  onRequested,
}: {
  slug: string;
  refreshing: boolean;
  onRequested: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const jobs = useJobs(slug, refreshing);
  const run = useRunJob(slug);
  const name = useJobName();

  const onRun = async (job: Job) => {
    const confirmed = await confirm({
      title: t("app.platform.jobs.runTitle", { name: name(job.code) }),
      description: t("app.platform.jobs.runText"),
      confirmLabel: t("app.platform.jobs.run"),
    });
    if (!confirmed) {
      return;
    }

    await run.mutateAsync(job.code).then(
      () => {
        onRequested();
        notify.success("app.platform.jobs.requested");
      },
      (error: unknown) => notify.error(error),
    );
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("app.platform.jobs.listTitle")}</CardTitle>
        <CardDescription>{t("app.platform.jobs.listDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        {jobs.error ? (
          <ApiErrorAlert error={jobs.error} onRetry={() => void jobs.refetch()} />
        ) : !jobs.data ? (
          <Skeleton className="h-24 w-full" />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("app.platform.jobs.jobColumn")}</TableHead>
                <TableHead>{t("app.platform.jobs.frequency")}</TableHead>
                <TableHead>{t("app.platform.jobs.lastRun")}</TableHead>
                <TableHead>
                  <span className="sr-only">{t("app.platform.jobs.actions")}</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {jobs.data.map((job) => (
                <TableRow key={job.code}>
                  <TableCell className="whitespace-normal">
                    <div className="font-medium">{name(job.code)}</div>
                    <div className="text-sm text-muted-foreground">{description(t, job.code)}</div>
                  </TableCell>
                  <TableCell className="whitespace-normal">{frequency(t, job.code)}</TableCell>
                  <TableCell className="whitespace-normal">
                    {job.lastRun ? (
                      <RunSummary run={job.lastRun} />
                    ) : (
                      <span className="text-muted-foreground">{t("app.platform.jobs.never")}</span>
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={job.isRunning || run.isPending}
                      onClick={() => void onRun(job)}
                      aria-label={t("app.platform.jobs.runNamed", { name: name(job.code) })}
                    >
                      <PlayIcon aria-hidden /> {t("app.platform.jobs.run")}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function RunsCard({ slug, refreshing }: { slug: string; refreshing: boolean }) {
  const t = useTranslations();
  const format = useFormatter();
  const runs = useJobRuns(slug, refreshing);
  const name = useJobName();

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("app.platform.jobs.runsTitle")}</CardTitle>
        <CardDescription>{t("app.platform.jobs.runsDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        {runs.error ? (
          <ApiErrorAlert error={runs.error} onRetry={() => void runs.refetch()} />
        ) : !runs.data ? (
          <Skeleton className="h-24 w-full" />
        ) : runs.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.platform.jobs.noRuns")}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("app.platform.jobs.jobColumn")}</TableHead>
                <TableHead>{t("app.platform.jobs.startedAt")}</TableHead>
                <TableHead>{t("app.platform.jobs.finishedAt")}</TableHead>
                <TableHead>{t("app.platform.jobs.actor")}</TableHead>
                <TableHead>{t("Status")}</TableHead>
                <TableHead>{t("app.platform.jobs.summary")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {runs.data.map((item) => (
                <TableRow key={item.id}>
                  <TableCell>{name(item.jobCode)}</TableCell>
                  <TableCell>
                    {format.dateTime(new Date(item.startedAt), {
                      dateStyle: "short",
                      timeStyle: "medium",
                    })}
                  </TableCell>
                  <TableCell>
                    {item.finishedAt
                      ? format.dateTime(new Date(item.finishedAt), {
                          dateStyle: "short",
                          timeStyle: "medium",
                        })
                      : "—"}
                  </TableCell>
                  <TableCell>
                    <Actor run={item} />
                  </TableCell>
                  <TableCell>
                    <StatusBadge status={item.status} />
                  </TableCell>
                  <TableCell className="whitespace-normal">
                    {item.errorCode ? <code className="mr-1 text-xs">{item.errorCode}</code> : null}
                    {item.summary}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function RunSummary({ run }: { run: JobRun }) {
  const format = useFormatter();
  return (
    <div className="flex flex-col gap-1 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge status={run.status} />
        <span>
          {format.dateTime(new Date(run.startedAt), { dateStyle: "short", timeStyle: "short" })}
        </span>
      </div>
      <div className="text-muted-foreground">
        <Actor run={run} />
        {run.summary ? ` — ${run.summary}` : null}
      </div>
    </div>
  );
}

function Actor({ run }: { run: JobRun }) {
  const t = useTranslations();
  switch (run.actorType) {
    case "Platform":
      return (
        <>
          {t("app.platform.jobs.actors.platform", {
            name: run.actorName ?? t("app.platform.jobs.actors.unknown"),
          })}
        </>
      );
    case "System":
      return <>{t("app.platform.jobs.actors.system")}</>;
    default:
      return <>{t("app.platform.jobs.actors.user")}</>;
  }
}

function StatusBadge({ status }: { status: string }) {
  const t = useTranslations();
  const variant =
    status === "Failed" ? "destructive" : status === "Running" ? "outline" : "secondary";
  const key = `app.platform.jobs.status.${status}`;
  return <Badge variant={variant}>{t.has(key) ? t(key) : status}</Badge>;
}

/** The translated name of a job, or its code for a job without translations. */
function useJobName() {
  const t = useTranslations();
  return (code: string) => {
    const key = `app.platform.jobs.catalog.${jobKey(code)}.name`;
    return t.has(key) ? t(key) : code;
  };
}

function description(t: ReturnType<typeof useTranslations>, code: string) {
  const key = `app.platform.jobs.catalog.${jobKey(code)}.description`;
  return t.has(key) ? t(key) : null;
}

function frequency(t: ReturnType<typeof useTranslations>, code: string) {
  const key = `app.platform.jobs.catalog.${jobKey(code)}.frequency`;
  return t.has(key) ? t(key) : null;
}
