"use client";

import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";

/** The status of a case with its text (F09); a completed rejected case shows "Rejected" (legacy grid). */
export function CaseStatusBadge({ status, rejected }: { status: string; rejected?: boolean }) {
  const t = useTranslations();
  if (status === "Completed") {
    return rejected ? (
      <Badge variant="destructive">{t("Rejected")}</Badge>
    ) : (
      <Badge variant="secondary">{t("Completed")}</Badge>
    );
  }

  return (
    <Badge variant="outline">
      {t(status === "InProgress" ? "InProgress" : status === "Sent" ? "Sent" : "Inserted")}
    </Badge>
  );
}
