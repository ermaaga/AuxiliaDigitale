"use client";

import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";

/** Business status of a client (D-05, Q03): Active while a case is open; text, not colour alone. */
export function ClientStatusBadge({ status }: { status: string }) {
  const t = useTranslations();
  return status === "Active" ? (
    <Badge>{t("Active")}</Badge>
  ) : (
    <Badge variant="outline">{t("Inactive")}</Badge>
  );
}
