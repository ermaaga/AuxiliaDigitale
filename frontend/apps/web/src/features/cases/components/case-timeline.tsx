"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";

import type { CaseDetail } from "../api";

/** The timeline of a case (status history: who, when, from, to, note), newest first. */
export function CaseTimeline({ value }: { value: CaseDetail }) {
  const t = useTranslations();
  const format = useFormatter();

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.cases.timeline")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <ol className="flex flex-col gap-3 border-l pl-4">
          {[...value.history].reverse().map((change, index) => (
            <li key={`${change.changedAt}-${index}`} className="relative text-sm">
              <span
                aria-hidden
                className="absolute -left-[21px] top-1.5 size-2.5 rounded-full bg-primary"
              />
              <p className="font-medium">
                {change.fromStatus
                  ? t("app.cases.changed", { from: t(change.fromStatus), to: t(change.toStatus) })
                  : t("app.cases.openedOn")}
              </p>
              <p className="text-muted-foreground">
                {format.dateTime(new Date(change.changedAt), {
                  dateStyle: "medium",
                  timeStyle: "short",
                })}
                {change.changedBy ? ` · ${change.changedBy.fullName}` : ""}
              </p>
              {change.note ? <p>{change.note}</p> : null}
            </li>
          ))}
        </ol>
      </CardContent>
    </Card>
  );
}
