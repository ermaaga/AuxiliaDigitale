"use client";

import { useTranslations } from "next-intl";

import type { ImportJob } from "../../imports-api";

/** Progress of an import (F19): a bar with its percentage and, unless compact, the row counters. */
export function ImportProgress({ job, compact = false }: { job: ImportJob; compact?: boolean }) {
  const t = useTranslations();
  const progress = Number(job.progress);
  return (
    <div className="flex min-w-32 flex-col gap-1">
      <div
        role="progressbar"
        aria-label={t("app.platform.imports.progressOf", { name: job.name })}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={progress}
        className="h-2 w-full overflow-hidden rounded-full bg-muted"
      >
        <div
          className="h-full bg-primary transition-[width] motion-reduce:transition-none"
          style={{ width: `${progress}%` }}
        />
      </div>
      <span className="text-xs text-muted-foreground">
        {compact
          ? `${progress}%`
          : t("app.platform.imports.counters", {
              total: job.totalRows,
              success: job.successRows,
              failed: job.failedRows,
            })}
      </span>
    </div>
  );
}
