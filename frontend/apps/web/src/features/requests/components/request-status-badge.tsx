"use client";

import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";

const VARIANTS = { Pending: "outline", Responded: "default", Closed: "secondary" } as const;

/** The status of a request with its text (F15). */
export function RequestStatusBadge({ status }: { status: string }) {
  const t = useTranslations();
  const variant = VARIANTS[status as keyof typeof VARIANTS] ?? "outline";
  return (
    <Badge variant={variant}>
      {t(`app.requests.status.${status}` as "app.requests.status.Pending")}
    </Badge>
  );
}
