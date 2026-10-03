"use client";

import { CheckIcon } from "lucide-react";
import { useTranslations } from "next-intl";

import { CASE_STATUSES } from "../api";

/** The status stepper of a case (skill auxilia-ui-design, "Case detail"): done, current and next steps with text. */
export function CaseStepper({ status }: { status: string }) {
  const t = useTranslations();
  const current = CASE_STATUSES.indexOf(status as (typeof CASE_STATUSES)[number]);

  return (
    <ol className="grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label={t("app.cases.progress")}>
      {CASE_STATUSES.map((step, index) => {
        const done = index < current || status === "Completed";
        const active = index === current;
        return (
          <li
            key={step}
            aria-current={active ? "step" : undefined}
            className={`flex items-center gap-2 rounded-md border px-3 py-2 text-sm ${active ? "border-primary bg-accent font-semibold" : done ? "text-foreground" : "text-muted-foreground"}`}
          >
            <span
              aria-hidden
              className={`flex size-6 shrink-0 items-center justify-center rounded-full border text-xs ${done ? "bg-primary text-primary-foreground" : ""}`}
            >
              {done ? <CheckIcon className="size-3.5" /> : index + 1}
            </span>
            {t(step)}
            {done && !active ? <span className="sr-only">{t("app.cases.stepDone")}</span> : null}
          </li>
        );
      })}
    </ol>
  );
}
