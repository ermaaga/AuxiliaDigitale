"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { postToBff } from "@/lib/api/browser";
import { tenantHref } from "@/lib/href";

import { FieldErrors, fieldErrorKeys } from "./field-errors";
import { PasswordInput } from "./password-input";
import { PasswordPolicy } from "./password-policy";

/** Expired password (F35, `AUX-12043` at sign-in): current and new password; on success the user is signed in. */
export function ExpiredPasswordForm({
  tenant,
  userName: initialUser,
}: {
  tenant: string;
  userName: string;
}) {
  const t = useTranslations();
  const router = useRouter();
  const [userName, setUserName] = React.useState(initialUser);
  const [current, setCurrent] = React.useState("");
  const [password, setPassword] = React.useState("");
  const [confirm, setConfirm] = React.useState("");
  const [mismatch, setMismatch] = React.useState(false);
  const [error, setError] = React.useState<unknown>();
  const [busy, setBusy] = React.useState(false);

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
      await postToBff("/api/auth/password/change", {
        tenant,
        userName,
        currentPassword: current,
        newPassword: password,
      });
      router.replace(tenantHref(tenant));
      router.refresh();
    } catch (failure) {
      setError(failure);
      setBusy(false);
    }
  }

  const passwordErrors = fieldErrorKeys(error, "newPassword");
  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      {error ? <ApiErrorAlert error={error} /> : null}
      <div className="flex flex-col gap-2">
        <Label htmlFor="expired-user">{t("Username")}</Label>
        <Input
          id="expired-user"
          autoComplete="username"
          required
          value={userName}
          onChange={(event) => setUserName(event.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="expired-current">{t("CurrentPassword")}</Label>
        <PasswordInput
          id="expired-current"
          autoComplete="current-password"
          required
          value={current}
          onChange={(event) => setCurrent(event.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="expired-new">{t("NewPassword")}</Label>
        <PasswordInput
          id="expired-new"
          autoComplete="new-password"
          required
          value={password}
          aria-invalid={passwordErrors.length > 0 || undefined}
          aria-describedby="expired-new-rules expired-new-errors"
          onChange={(event) => setPassword(event.target.value)}
        />
        <FieldErrors id="expired-new-errors" keys={passwordErrors} />
        <PasswordPolicy tenant={tenant} id="expired-new-rules" />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="expired-confirm">{t("ConfirmNewPassword")}</Label>
        <PasswordInput
          id="expired-confirm"
          autoComplete="new-password"
          required
          value={confirm}
          aria-invalid={mismatch || undefined}
          aria-describedby="expired-confirm-error"
          onChange={(event) => setConfirm(event.target.value)}
        />
        {mismatch ? (
          <p id="expired-confirm-error" className="text-sm text-destructive">
            {t("app.auth.passwordMismatch")}
          </p>
        ) : null}
      </div>
      <Button type="submit" disabled={busy}>
        {t("ChangePassword")}
      </Button>
    </form>
  );
}
