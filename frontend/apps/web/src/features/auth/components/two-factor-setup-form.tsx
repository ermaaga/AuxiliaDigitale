"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { useTheme } from "@auxilia/ui/components/theme-provider";
import type { components } from "@auxilia/api-client";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { TotpEnrollment } from "@/components/totp-enrollment";
import { postToBff } from "@/lib/api/browser";
import { applyProfilePreferences } from "@/features/profile";
import { TwoFactorCodeInput, normalizeCode } from "@/features/two-factor";
import { tenantHref } from "@/lib/href";

import { PasswordInput } from "./password-input";

type Enrollment = components["schemas"]["TwoFactorEnrollmentResponse"];

/**
 * Required enrolment of the authenticator app (N04): user name and password start it (the secret is shown once, never
 * stored by the page), a code of the app confirms it and signs in. Reloading starts a new enrolment.
 */
export function TwoFactorSetupForm({
  tenant,
  userName: initialUser,
  rememberMeDays,
}: {
  tenant: string;
  userName: string;
  rememberMeDays: number;
}) {
  const t = useTranslations();
  const router = useRouter();
  const { setTheme } = useTheme();
  const [userName, setUserName] = React.useState(initialUser);
  const [password, setPassword] = React.useState("");
  const [enrollment, setEnrollment] = React.useState<Enrollment>();
  const [code, setCode] = React.useState("");
  const [stayIn, setStayIn] = React.useState(false);
  const [error, setError] = React.useState<unknown>();
  const [busy, setBusy] = React.useState(false);

  async function start(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      setEnrollment(
        await postToBff<Enrollment>("/api/auth/two-factor/setup", { tenant, userName, password }),
      );
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  }

  async function confirm(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      await postToBff("/api/auth/two-factor/confirm", {
        tenant,
        userName,
        password,
        code: normalizeCode(code),
        rememberMe: rememberMeDays > 0 && stayIn,
      });
      await applyProfilePreferences(setTheme);
      router.replace(tenantHref(tenant));
      router.refresh();
    } catch (failure) {
      setError(failure);
      setBusy(false);
    }
  }

  if (enrollment) {
    return (
      <form className="flex flex-col gap-4" onSubmit={(event) => void confirm(event)}>
        {error ? <ApiErrorAlert error={error} /> : null}
        <TotpEnrollment secret={enrollment.secret} uri={enrollment.uri} />
        <TwoFactorCodeInput id="setup-code" value={code} onChange={setCode} />
        {rememberMeDays > 0 ? (
          <div className="flex items-center gap-2">
            <Checkbox
              id="setup-stay"
              checked={stayIn}
              onCheckedChange={(value) => setStayIn(value === true)}
            />
            <Label htmlFor="setup-stay">
              {t("app.auth.staySignedIn", { days: rememberMeDays })}
            </Label>
          </div>
        ) : null}
        <Button type="submit" disabled={busy}>
          {t("app.twoFactor.activateAndSignIn")}
        </Button>
      </form>
    );
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={(event) => void start(event)}>
      {error ? <ApiErrorAlert error={error} /> : null}
      <div className="flex flex-col gap-2">
        <Label htmlFor="setup-user">{t("Username")}</Label>
        <Input
          id="setup-user"
          autoComplete="username"
          required
          value={userName}
          onChange={(event) => setUserName(event.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="setup-password">{t("Password")}</Label>
        <PasswordInput
          id="setup-password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </div>
      <Button type="submit" disabled={busy}>
        {t("app.twoFactor.continue")}
      </Button>
      <Button variant="link" asChild>
        <Link href={tenantHref(tenant, "/login")}>{t("app.twoFactor.backToLogin")}</Link>
      </Button>
    </form>
  );
}
