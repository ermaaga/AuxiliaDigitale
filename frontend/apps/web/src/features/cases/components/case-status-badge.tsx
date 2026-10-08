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

/** The validity a client sees on their own cases (F09, legacy `/client/subscriptions`): Active, Expired or Inactive. */
export function CaseValidityBadge({ validity }: { validity: string }) {
  const t = useTranslations();
  if (validity === "Active") {
    return <Badge variant="secondary">{t("Active")}</Badge>;
  }

  return validity === "Expired" ? (
    <Badge variant="destructive">{t("Expired")}</Badge>
  ) : (
    <Badge variant="outline">{t("Inactive")}</Badge>
  );
}
