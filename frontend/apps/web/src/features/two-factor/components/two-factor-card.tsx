"use client";

import * as React from "react";
import Link from "next/link";
import { ShieldCheckIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Alert, AlertDescription, AlertTitle } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { TotpEnrollment } from "@/components/totp-enrollment";
import { PasswordInput } from "@/features/auth/components/password-input";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import { useTwoFactorMutations, useTwoFactorStatus, type TwoFactorEnrollment } from "../api";
import { TwoFactorCodeInput } from "./two-factor-code-input";

/**
 * The own authenticator app (N04, profile): status, enrolment with QR code and a confirming code, disable with the
 * password (not when the roles require it). `suggest` is the first visit after the temporary password was changed: the
 * setup is offered with "Later".
 */
export function TwoFactorCard({ tenant, suggest = false }: { tenant: string; suggest?: boolean }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const status = useTwoFactorStatus(tenant);
  const { begin, confirm, disable } = useTwoFactorMutations(tenant);
  const [enrollment, setEnrollment] = React.useState<TwoFactorEnrollment>();
  const [disabling, setDisabling] = React.useState(false);
  const [code, setCode] = React.useState("");
  const [password, setPassword] = React.useState("");
  const [error, setError] = React.useState<unknown>();

  if (status.error) {
    return <ApiErrorAlert error={status.error} onRetry={() => void status.refetch()} />;
  }

  if (!status.data) {
    return <Skeleton className="h-40 w-full" />;
  }

  const { enabled, required, enabledAt } = status.data;

  const start = async () => {
    setError(undefined);
    try {
      setEnrollment(await begin.mutateAsync());
    } catch (failure) {
      setError(failure);
    }
  };

  const activate = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(undefined);
    try {
      await confirm.mutateAsync(code);
      setEnrollment(undefined);
      setCode("");
      notify.success("app.twoFactor.enabledToast");
    } catch (failure) {
      setError(failure);
    }
  };

  const turnOff = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(undefined);
    try {
      await disable.mutateAsync(password);
      setDisabling(false);
      setPassword("");
      notify.success("app.twoFactor.disabledToast");
    } catch (failure) {
      setError(failure);
    }
  };

  return (
    <Card id="two-factor">
      <CardHeader>
        <CardTitle>
          <h2 className="flex items-center gap-2 text-base font-semibold">
            <ShieldCheckIcon aria-hidden className="size-4" />
            {t("app.twoFactor.title")}
          </h2>
        </CardTitle>
        <CardDescription>{t("app.twoFactor.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {error ? <ApiErrorAlert error={error} /> : null}
        {suggest && !enabled && !enrollment ? (
          <Alert>
            <AlertTitle>{t("app.twoFactor.suggestTitle")}</AlertTitle>
            <AlertDescription>{t("app.twoFactor.suggestText")}</AlertDescription>
          </Alert>
        ) : null}
        <div className="flex flex-wrap items-center gap-2 text-sm">
          {enabled ? (
            <>
              <Badge variant="secondary">{t("app.twoFactor.active")}</Badge>
              {enabledAt ? (
                <span className="text-muted-foreground">
                  {t("app.twoFactor.activeSince", {
                    date: format.dateTime(new Date(enabledAt), { dateStyle: "medium" }),
                  })}
                </span>
              ) : null}
            </>
          ) : (
            <Badge variant="outline">{t("app.twoFactor.inactive")}</Badge>
          )}
          {required ? (
            <span className="text-muted-foreground">{t("app.twoFactor.requiredByRole")}</span>
          ) : null}
        </div>

        {enrollment ? (
          <form className="flex flex-col gap-4" onSubmit={(event) => void activate(event)}>
            <TotpEnrollment secret={enrollment.secret} uri={enrollment.uri} />
            <TwoFactorCodeInput id="two-factor-enrol-code" value={code} onChange={setCode} />
            <div className="flex flex-wrap justify-end gap-2">
              <Button type="button" variant="outline" onClick={() => setEnrollment(undefined)}>
                {t("Cancel")}
              </Button>
              <Button type="submit" disabled={confirm.isPending}>
                {t("app.twoFactor.activate")}
              </Button>
            </div>
          </form>
        ) : disabling ? (
          <form className="flex flex-col gap-4" onSubmit={(event) => void turnOff(event)}>
            <p className="text-sm text-muted-foreground">{t("app.twoFactor.disableText")}</p>
            <div className="flex flex-col gap-2 sm:max-w-[calc(50%-0.5rem)]">
              <label htmlFor="two-factor-password" className="text-sm font-medium">
                {t("CurrentPassword")}
              </label>
              <PasswordInput
                id="two-factor-password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </div>
            <div className="flex flex-wrap justify-end gap-2">
              <Button type="button" variant="outline" onClick={() => setDisabling(false)}>
                {t("Cancel")}
              </Button>
              <Button type="submit" variant="destructive" disabled={disable.isPending}>
                {t("app.twoFactor.disable")}
              </Button>
            </div>
          </form>
        ) : (
          <div className="flex flex-wrap justify-end gap-2">
            {suggest && !enabled ? (
              <Button variant="outline" asChild>
                <Link href={tenantHref(tenant)}>{t("app.twoFactor.later")}</Link>
              </Button>
            ) : null}
            {enabled ? (
              required ? null : (
                <Button variant="outline" onClick={() => setDisabling(true)}>
                  {t("app.twoFactor.disable")}
                </Button>
              )
            ) : (
              <Button onClick={() => void start()} disabled={begin.isPending}>
                {t("app.twoFactor.setUp")}
              </Button>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
