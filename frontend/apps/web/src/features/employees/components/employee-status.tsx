"use client";

import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";

/** Legacy Active/Inactive (= can sign in) and the "default" badge of the default employee (Q31). */
export function EmployeeStatus({
  canSignIn,
  isDefault,
}: {
  canSignIn: boolean;
  isDefault: boolean;
}) {
  const t = useTranslations();
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      {canSignIn ? (
        <Badge variant="secondary">{t("Active")}</Badge>
      ) : (
        <Badge variant="outline">{t("Inactive")}</Badge>
      )}
      {isDefault ? <Badge>{t("DefaultEmployee")}</Badge> : null}
    </span>
  );
}
