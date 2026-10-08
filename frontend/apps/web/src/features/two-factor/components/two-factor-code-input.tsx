"use client";

import { useTranslations } from "next-intl";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";

/** The 6-digit code of the authenticator app (N04): numeric keyboard, filled by the OS from the app when it can. */
export function TwoFactorCodeInput({
  id,
  value,
  onChange,
  autoFocus,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  autoFocus?: boolean;
}) {
  const t = useTranslations();
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{t("app.twoFactor.codeLabel")}</Label>
      <Input
        id={id}
        name="totp"
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern="[0-9 ]*"
        maxLength={7}
        required
        autoFocus={autoFocus}
        aria-describedby={`${id}-hint`}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      <p id={`${id}-hint`} className="text-sm text-muted-foreground">
        {t("app.twoFactor.codeHint")}
      </p>
    </div>
  );
}
