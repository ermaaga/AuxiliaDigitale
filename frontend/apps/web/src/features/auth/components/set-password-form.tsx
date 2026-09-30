"use client";

import * as React from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { unwrap } from "@auxilia/api-client";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Button } from "@auxilia/ui/components/button";
import { Label } from "@auxilia/ui/components/label";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { createBffClient } from "@/lib/api/client";
import { tenantHref } from "@/lib/href";

import { FieldErrors, fieldErrorKeys } from "./field-errors";
import { PasswordInput } from "./password-input";
import { PasswordPolicy } from "./password-policy";

/**
 * New password from an e-mail link (D-06 activation, password reset): the token of the link, the password twice, the
 * tenant's rules. The API validates the policy and the history; its field errors are shown under the field.
 */
export function SetPasswordForm({
  tenant,
  token,
  kind,
}: {
  tenant: string;
  token: string | undefined;
  kind: "activate" | "reset";
}) {
  const t = useTranslations();
  const [password, setPassword] = React.useState("");
  const [confirm, setConfirm] = React.useState("");
  const [error, setError] = React.useState<unknown>();
  const [mismatch, setMismatch] = React.useState(false);
  const [done, setDone] = React.useState(false);
  const [busy, setBusy] = React.useState(false);

  if (!token) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{t("app.auth.linkMissing")}</AlertDescription>
      </Alert>
    );
  }

  if (done) {
    return (
      <div className="flex flex-col gap-4" role="status">
        <p>{t(kind === "activate" ? "app.auth.activate.success" : "app.auth.reset.success")}</p>
        <Button asChild>
          <Link href={tenantHref(tenant, "/login")}>{t("Login")}</Link>
        </Button>
      </div>
    );
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const different = password !== confirm;
    setMismatch(different);
    if (different) {
      return;
    }

    setBusy(true);
    setError(undefined);
    try {
      const client = createBffClient("tenant", { tenant });
      const body = { token: token!, password };
      unwrap(
        kind === "activate"
          ? await client.POST("/api/v1/auth/activate", { body })
          : await client.POST("/api/v1/auth/password/reset", { body }),
      );
      setDone(true);
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  }

  const passwordErrors = fieldErrorKeys(error, "password");
  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      {error ? <ApiErrorAlert error={error} /> : null}
      <div className="flex flex-col gap-2">
        <Label htmlFor="new-password">{t("NewPassword")}</Label>
        <PasswordInput
          id="new-password"
          autoComplete="new-password"
          required
          value={password}
          aria-invalid={passwordErrors.length > 0 || undefined}
          aria-describedby="new-password-rules new-password-errors"
          onChange={(event) => setPassword(event.target.value)}
        />
        <FieldErrors id="new-password-errors" keys={passwordErrors} />
        <PasswordPolicy tenant={tenant} id="new-password-rules" />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="confirm-password">{t("ConfirmPassword")}</Label>
        <PasswordInput
          id="confirm-password"
          autoComplete="new-password"
          required
          value={confirm}
          aria-invalid={mismatch || undefined}
          aria-describedby="confirm-password-error"
          onChange={(event) => setConfirm(event.target.value)}
        />
        {mismatch ? (
          <p id="confirm-password-error" className="text-sm text-destructive">
            {t("app.auth.passwordMismatch")}
          </p>
        ) : null}
      </div>
      <Button type="submit" disabled={busy}>
        {kind === "activate" ? t("app.auth.activate.submit") : t("ResetPassword")}
      </Button>
      <Link
        className="text-center text-sm text-primary-text underline-offset-4 hover:underline"
        href={tenantHref(tenant, "/login")}
      >
        {t("app.auth.backToLogin")}
      </Link>
    </form>
  );
}
