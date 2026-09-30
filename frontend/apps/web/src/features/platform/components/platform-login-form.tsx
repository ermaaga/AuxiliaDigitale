"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { PasswordInput } from "@/features/auth/components/password-input";
import { postToBff } from "@/lib/api/browser";
import { platformHref } from "@/lib/href";

/** Wrong e-mail, password or code look alike (the API never says which one was wrong). */
const LOGIN_MESSAGES = { "AUX-12002": "app.platform.login.failed" } as const;

/**
 * Console sign-in (N02, D-22): e-mail, password and the current code of the authenticator app, in one step. The
 * console BFF keeps the tokens in its own session (`__Host-aux_psid`), separate from any tenant session.
 */
export function PlatformLoginForm({ next }: { next: string }) {
  const t = useTranslations();
  const router = useRouter();
  const [email, setEmail] = React.useState("");
  const [password, setPassword] = React.useState("");
  const [code, setCode] = React.useState("");
  const [error, setError] = React.useState<unknown>();
  const [busy, setBusy] = React.useState(false);

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      await postToBff("/api/platform-auth/login", {
        grantType: "password",
        email,
        password,
        code: code.replace(/\s/g, ""),
      });
      router.replace(next);
      router.refresh();
    } catch (failure) {
      // A wrong code is spent either way: the field is cleared for the next one.
      setCode("");
      setError(failure);
      setBusy(false);
    }
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      {error ? <ApiErrorAlert error={error} messageKeys={LOGIN_MESSAGES} /> : null}
      <div className="flex flex-col gap-2">
        <Label htmlFor="platform-email">{t("Email")}</Label>
        <Input
          id="platform-email"
          name="email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="platform-password">{t("Password")}</Label>
        <PasswordInput
          id="platform-password"
          name="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="platform-code">{t("app.platform.login.code")}</Label>
        <Input
          id="platform-code"
          name="code"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9 ]{6,7}"
          maxLength={7}
          required
          value={code}
          onChange={(event) => setCode(event.target.value)}
        />
      </div>
      <Button type="submit" disabled={busy}>
        {t("Login")}
      </Button>
      <Link
        className="text-center text-sm text-primary-text underline-offset-4 hover:underline"
        href={platformHref("/activate")}
      >
        {t("app.platform.login.activateLink")}
      </Link>
    </form>
  );
}
