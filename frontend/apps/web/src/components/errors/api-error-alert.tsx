"use client";

import * as React from "react";
import { CircleAlertIcon, CopyIcon, RotateCcwIcon } from "lucide-react";
import { isApiError } from "@auxilia/api-client";
import { Alert, AlertDescription, AlertTitle } from "@auxilia/ui/components/alert";
import { Button } from "@auxilia/ui/components/button";

/** Translated texts of the alert (the component has none of its own, skill auxilia-localization). */
export type ApiErrorLabels = { title: string; retry: string; copy: string; trace: string };

type Translate = (key: string) => string | undefined;

/**
 * The message the user sees for an error: `errors.<AUX code>` when translated, otherwise `errors.generic`
 * (skill auxilia-localization); the code is always shown next to it.
 */
export function errorMessage(error: unknown, translate: Translate): string {
  const key = isApiError(error) ? error.messageKey : "errors.generic";
  return translate(key) ?? translate("errors.generic") ?? key;
}

/** Error state of a query or form (skill auxilia-ui-design): message, copyable `AUX-` code, trace id, retry. */
export function ApiErrorAlert({
  error,
  translate,
  labels,
  onRetry,
}: {
  error: unknown;
  translate: Translate;
  labels: ApiErrorLabels;
  onRetry?: () => void;
}) {
  const code = isApiError(error) ? error.errorCode : "AUX-WEB-UNKNOWN";
  const traceId = isApiError(error) ? error.traceId : undefined;
  const reference = traceId ? `${code} · ${traceId}` : code;

  return (
    <Alert variant="destructive" role="alert">
      <CircleAlertIcon aria-hidden />
      <AlertTitle>{labels.title}</AlertTitle>
      <AlertDescription className="flex flex-col gap-3">
        <p>{errorMessage(error, translate)}</p>
        <p className="flex flex-wrap items-center gap-2 text-xs">
          <code className="rounded bg-muted px-1.5 py-0.5 text-foreground" data-testid="error-code">
            {code}
          </code>
          {traceId ? (
            <span className="text-muted-foreground">
              {labels.trace}: <code>{traceId}</code>
            </span>
          ) : null}
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            aria-label={labels.copy}
            onClick={() => void navigator.clipboard?.writeText(reference)}
          >
            <CopyIcon aria-hidden />
          </Button>
        </p>
        {onRetry ? (
          <div>
            <Button type="button" variant="outline" size="sm" onClick={onRetry}>
              <RotateCcwIcon aria-hidden /> {labels.retry}
            </Button>
          </div>
        ) : null}
      </AlertDescription>
    </Alert>
  );
}
