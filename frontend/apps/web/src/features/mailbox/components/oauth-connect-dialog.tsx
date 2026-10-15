"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Label } from "@auxilia/ui/components/label";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";

import {
  startOAuth,
  type ConnectedAccount,
  type ConnectedAccountProvider,
  type OAuthProvider,
} from "../api";
import { capabilitiesSchema } from "../schemas";
import { providerLabel } from "./provider";

/**
 * "Sign in with Google / Microsoft" (N05, ADR 0021): the user picks mail and/or calendar (only what is chosen is
 * asked to the provider), then the browser goes to the provider's consent page; it comes back to the BFF callback.
 * With `account` it reconnects that account or extends its access.
 */
export function OAuthConnectDialog({
  provider,
  account,
  open,
  onOpenChange,
}: {
  provider: ConnectedAccountProvider & { provider: OAuthProvider };
  account?: ConnectedAccount;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const t = useTranslations();
  const name = providerLabel(t, provider.provider);
  const [mail, setMail] = React.useState(account ? account.hasMail : provider.supportsMail);
  const [calendar, setCalendar] = React.useState(
    account ? account.hasCalendar : provider.supportsCalendar,
  );
  const [pending, setPending] = React.useState(false);
  const [error, setError] = React.useState<unknown>();
  const valid = capabilitiesSchema.safeParse({ mail, calendar }).success;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!valid) {
      return;
    }

    setError(undefined);
    setPending(true);
    try {
      const url = await startOAuth(provider.provider, { mail, calendar, accountId: account?.id });
      window.location.assign(url);
    } catch (failure) {
      setError(failure);
      setPending(false);
    }
  };

  const option = (
    id: string,
    checked: boolean,
    onChange: (value: boolean) => void,
    label: string,
    hint: string,
  ) => (
    <div className="flex items-start gap-3">
      <Checkbox
        id={id}
        checked={checked}
        onCheckedChange={(value) => onChange(value === true)}
        aria-describedby={`${id}-hint`}
        className="mt-0.5"
      />
      <div className="flex flex-col gap-1">
        <Label htmlFor={id}>{label}</Label>
        <p id={`${id}-hint`} className="text-sm text-muted-foreground">
          {hint}
        </p>
      </div>
    </div>
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent closeLabel={t("Close")} className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>
            {account
              ? t("app.connectedAccounts.reconnectTitle", { address: account.address })
              : t("app.connectedAccounts.oauthTitle", { provider: name })}
          </DialogTitle>
          <DialogDescription>{t("app.connectedAccounts.oauthDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
          {error ? <ApiErrorAlert error={error} /> : null}
          <fieldset className="flex flex-col gap-4">
            <legend className="sr-only">{t("app.connectedAccounts.access")}</legend>
            {provider.supportsMail
              ? option(
                  "oauth-mail",
                  mail,
                  setMail,
                  t("app.connectedAccounts.mail"),
                  t("app.connectedAccounts.mailHint"),
                )
              : null}
            {provider.supportsCalendar
              ? option(
                  "oauth-calendar",
                  calendar,
                  setCalendar,
                  t("app.connectedAccounts.calendar"),
                  t("app.connectedAccounts.calendarHint"),
                )
              : null}
          </fieldset>
          {valid ? null : (
            <p role="alert" className="text-sm text-destructive">
              {t("validation.mailbox.capabilities")}
            </p>
          )}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("Cancel")}
            </Button>
            <Button type="submit" disabled={!valid || pending}>
              {t("app.connectedAccounts.continueTo", { provider: name })}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
