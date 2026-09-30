"use client";

import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";

/** Translation keys of the server-side errors of one field (`fieldErrors` of a 400, skill auxilia-frontend-feature). */
export function fieldErrorKeys(error: unknown, field: string): readonly string[] {
  return isApiError(error) ? (error.fieldErrors[field] ?? []) : [];
}

/** The translated errors of a field, linked to its input with `aria-describedby={id}`. */
export function FieldErrors({ id, keys }: { id: string; keys: readonly string[] }) {
  const t = useTranslations();
  if (keys.length === 0) {
    return null;
  }

  return (
    <ul id={id} className="text-sm text-destructive">
      {keys.map((key) => (
        <li key={key}>{t.has(key) ? t(key) : t("validation.invalid")}</li>
      ))}
    </ul>
  );
}
