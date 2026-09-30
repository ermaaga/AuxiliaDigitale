"use client";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";

/** Unexpected error of a console page: translated message, code and retry (skill auxilia-ui-design). */
export default function AppError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return <ApiErrorAlert error={error} onRetry={reset} />;
}
