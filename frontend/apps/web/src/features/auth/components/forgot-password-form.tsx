"use client";

import * as React from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { unwrap } from "@auxilia/api-client";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { createBffClient } from "@/lib/api/client";
import { tenantHref } from "@/lib/href";

/** Password reset request: always the same answer, whether the user exists or not (F01, no enumeration). */
export function ForgotPasswordForm({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const [userName, setUserName] = React.useState("");
  const [sent, setSent] = React.useState(false);
  const [error, setError] = React.useState<unknown>();
  const [busy, setBusy] = React.useState(false);

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      unwrap(
        await createBffClient("tenant", { tenant }).POST("/api/v1/auth/password/forgot", {
          body: { userName },
        }),
      );
      setSent(true);
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  }

  const back = (
    <Link
      className="text-center text-sm text-primary-text underline-offset-4 hover:underline"
      href={tenantHref(tenant, "/login")}
    >
      {t("app.auth.backToLogin")}
    </Link>
  );

  if (sent) {
    return (
      <div className="flex flex-col gap-4">
        <p role="status">{t("ResetPasswordSent")}</p>
        {back}
      </div>
    );
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      {error ? <ApiErrorAlert error={error} /> : null}
      <div className="flex flex-col gap-2">
        <Label htmlFor="forgot-user">{t("Username")}</Label>
        <Input
          id="forgot-user"
          autoComplete="username"
          required
          value={userName}
          onChange={(event) => setUserName(event.target.value)}
        />
      </div>
      <Button type="submit" disabled={busy}>
        {t("app.auth.forgot.submit")}
      </Button>
      {back}
    </form>
  );
}
