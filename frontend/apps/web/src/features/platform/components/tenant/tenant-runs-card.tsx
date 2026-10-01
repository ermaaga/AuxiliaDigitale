"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";

import { runKindLabel, runStatusLabel } from "../../labels";
import type { TenantDetail } from "../../tenant-api";

/** Provisioning, migrations and jobs of the tenant, newest first (details in the logs, by code). */
export function TenantRunsCard({ runs }: { runs: TenantDetail["runs"] }) {
  const t = useTranslations();
  const format = useFormatter();

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.runs.title")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        {runs.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.platform.runs.empty")}</p>
        ) : (
          <ul className="flex flex-col divide-y">
            {runs.map((run) => (
              <li key={`${run.kind}-${run.startedAt}`} className="flex flex-col gap-1 py-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-sm font-medium">{runKindLabel(t, run.kind)}</span>
                  <Badge
                    variant={
                      run.status === "Failed"
                        ? "destructive"
                        : run.status === "Running"
                          ? "outline"
                          : "secondary"
                    }
                  >
                    {runStatusLabel(t, run.status)}
                  </Badge>
                </div>
                <span className="text-xs text-muted-foreground">
                  {format.dateTime(new Date(run.startedAt), {
                    dateStyle: "medium",
                    timeStyle: "short",
                  })}
                  {run.errorCode ? (
                    <>
                      {" "}
                      · <code>{run.errorCode}</code>
                    </>
                  ) : null}
                  {run.message ? <> · {run.message}</> : null}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
