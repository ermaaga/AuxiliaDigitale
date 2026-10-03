"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { isApiError, unwrap } from "@auxilia/api-client";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { useTheme } from "@auxilia/ui/components/theme-provider";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { postToBff } from "@/lib/api/browser";
import { createBffClient } from "@/lib/api/client";
import { applyProfilePreferences } from "@/features/profile";
import { tenantHref } from "@/lib/href";

import { PasswordInput } from "./password-input";

/** Key of the remembered user name (legacy "Remember username", F01/F34: kept in this browser only). */
export const rememberedUserKey = (tenant: string) => `aux_login_user:${tenant}`;

type Mode = "password" | "otp";

const noSubscription = () => () => {};

function readRemembered(tenant: string): string | null {
  try {
    return window.localStorage.getItem(rememberedUserKey(tenant));
  } catch {
    return null;
  }
}

/**
 * Sign-in (F01, F35): user name + password, or an e-mailed code when the tenant enables `email-otp`. The BFF keeps the
 * tokens; an expired password continues on the change page; the failure message never says which field was wrong.
 */
export function LoginForm({
  tenant,
  methods,
  next,
}: {
  tenant: string;
  methods: readonly string[];
  next: string;
}) {
  const t = useTranslations();
  const router = useRouter();
  const { setTheme } = useTheme();
  const [mode, setMode] = React.useState<Mode>("password");
  // The remembered user name is read from this browser after hydration (server snapshot: none).
  const remembered = React.useSyncExternalStore(
    noSubscription,
    () => readRemembered(tenant),
    () => null,
  );
  const [typedUserName, setUserName] = React.useState<string>();
  const [password, setPassword] = React.useState("");
  const [code, setCode] = React.useState("");
  const [codeSent, setCodeSent] = React.useState(false);
  const [rememberChoice, setRemember] = React.useState<boolean>();
  const [error, setError] = React.useState<unknown>();
  const [busy, setBusy] = React.useState(false);

  const userName = typedUserName ?? remembered ?? "";
  const remember = rememberChoice ?? remembered !== null;

  const otpEnabled = methods.includes("email-otp");

  async function sendCode() {
    setBusy(true);
    setError(undefined);
    try {
      unwrap(
        await createBffClient("tenant", { tenant }).POST("/api/v1/auth/otp", {
          body: { userName },
        }),
      );
      setCodeSent(true);
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  }

  function persistRememberedUser() {
    try {
      if (remember) {
        window.localStorage.setItem(rememberedUserKey(tenant), userName);
      } else {
        window.localStorage.removeItem(rememberedUserKey(tenant));
      }
    } catch {
      // Storage disabled: sign-in still works.
    }
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mode === "otp" && !codeSent) {
      await sendCode();
      return;
    }

    setBusy(true);
    setError(undefined);
    try {
      await postToBff(
        "/api/auth/login",
        mode === "password"
          ? { tenant, grantType: "password", userName, password }
          : { tenant, grantType: "email_otp", userName, code },
      );
      persistRememberedUser();
      await applyProfilePreferences(setTheme);
      router.replace(next);
      router.refresh();
    } catch (failure) {
      if (isApiError(failure) && failure.errorCode === "AUX-12043") {
        // The credentials were right: the choice is kept even though the sign-in continues on the change page.
        persistRememberedUser();
        router.push(
          `${tenantHref(tenant, "/password-expired")}?user=${encodeURIComponent(userName)}`,
        );
        return;
      }

      setError(failure);
      setBusy(false);
    }
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate={false}>
      {error ? <ApiErrorAlert error={error} /> : null}
      <div className="flex flex-col gap-2">
        <Label htmlFor="login-user">{t("Username")}</Label>
        <Input
          id="login-user"
          name="username"
          autoComplete="username"
          required
          value={userName}
          onChange={(event) => setUserName(event.target.value)}
        />
      </div>
      {mode === "password" ? (
        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between gap-2">
            <Label htmlFor="login-password">{t("Password")}</Label>
            <Link
              className="text-sm text-primary-text underline-offset-4 hover:underline"
              href={tenantHref(tenant, "/forgot-password")}
            >
              {t("ForgotPassword")}
            </Link>
          </div>
          <PasswordInput
            id="login-password"
            name="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </div>
      ) : codeSent ? (
        <div className="flex flex-col gap-2">
          <Label htmlFor="login-code">{t("app.auth.otp.codeLabel")}</Label>
          <Input
            id="login-code"
            name="code"
            inputMode="numeric"
            autoComplete="one-time-code"
            required
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
          <p className="text-sm text-muted-foreground" role="status">
            {t("OtpSent")}
          </p>
        </div>
      ) : null}
      <div className="flex items-center gap-2">
        <Checkbox
          id="login-remember"
          checked={remember}
          onCheckedChange={(value) => setRemember(value === true)}
        />
        <Label htmlFor="login-remember">{t("RememberUsername")}</Label>
      </div>
      <Button type="submit" disabled={busy}>
        {mode === "otp" && !codeSent ? t("SendOtp") : t("Login")}
      </Button>
      {otpEnabled ? (
        <Button
          type="button"
          variant="link"
          onClick={() => {
            setMode(mode === "password" ? "otp" : "password");
            setCodeSent(false);
            setError(undefined);
          }}
        >
          {mode === "password" ? t("OtpLogin") : t("LoginWithPassword")}
        </Button>
      ) : null}
    </form>
  );
}
