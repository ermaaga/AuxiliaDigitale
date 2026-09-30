"use client";

import { useTranslations } from "next-intl";
import { toast } from "sonner";

import { errorMessage } from "@/components/errors/api-error-alert";
import { isApiError } from "@auxilia/api-client";

/**
 * Toast feedback (F34, skill auxilia-ui-design): success for non-destructive actions, errors with the translated
 * message and the `AUX-` code. Texts are translation keys.
 */
export function useNotify() {
  const t = useTranslations();
  const translate = (key: string) => (t.has(key) ? t(key) : undefined);

  return {
    success: (key: string) => toast.success(translate(key) ?? key),
    info: (key: string) => toast.info(translate(key) ?? key),
    error: (error: unknown) =>
      toast.error(errorMessage(error, translate), {
        description: isApiError(error) ? error.errorCode : undefined,
      }),
  };
}
