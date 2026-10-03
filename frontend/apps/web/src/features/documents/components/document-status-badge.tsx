"use client";

import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";

/** The check of a stored file (F14): being checked, available, damaged. */
export function DocumentStatusBadge({ status }: { status: string }) {
  const t = useTranslations();
  if (status === "Available") {
    return null;
  }

  return status === "Damaged" ? (
    <Badge variant="destructive">{t("app.documents.status.damaged")}</Badge>
  ) : (
    <Badge variant="outline">{t("app.documents.status.processing")}</Badge>
  );
}
