"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
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
import { Label } from "@auxilia/ui/components/label";
import { Switch } from "@auxilia/ui/components/switch";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { useNotify } from "@/lib/notify";
import { UserTwoFactorReset } from "@/features/two-factor";
import { useCan } from "@/lib/permissions";

import {
  resetClientPassword,
  sendClientInvitation,
  setClientSignIn,
  useClientMutation,
  type ClientDetail,
} from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";

/**
 * The sign-in account of a client (D-05, D-06): sign-in on/off (enabling needs an employee in charge, Q60, `AUX-13019`),
 * activation e-mail while the client has no password, and the operator password reset (link or temporary password,
 * shown once).
 */
export function ClientAccess({ tenant, client }: { tenant: string; client: ClientDetail }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(DIRECTORY_PERMISSIONS.manageClients);
  const canReset = useCan(DIRECTORY_PERMISSIONS.resetClientPasswords);
  const [temporary, setTemporary] = React.useState<string | null>(null);
  const signIn = useClientMutation(tenant, (on: boolean) => setClientSignIn(client.id, on));
  const invite = useClientMutation(tenant, () => sendClientInvitation(client.id));
  const reset = useClientMutation(tenant, (sendLink: boolean) =>
    resetClientPassword(client.id, sendLink),
  );
  const account = client.account;

  if (!account) {
    return (
      <Alert>
        <AlertDescription>{t("app.clients.noAccount")}</AlertDescription>
      </Alert>
    );
  }

  const onSignIn = async (on: boolean) => {
    try {
      await signIn.mutateAsync(on);
      notify.success(on ? "app.clients.signInEnabled" : "app.clients.signInDisabledToast");
    } catch (error) {
      notify.error(error);
    }
  };

  const onInvite = async () => {
    try {
      const result = await invite.mutateAsync(undefined);
      if (result.sent) {
        notify.success("app.clients.invitationSent");
      } else {
        notify.info("app.clients.invitationNotSent");
      }
    } catch (error) {
      notify.error(error);
    }
  };

  const onReset = async (sendLink: boolean) => {
    const confirmed = await confirm({
      description: t(
        sendLink ? "app.clients.resetLinkConfirm" : "app.clients.resetTemporaryConfirm",
      ),
      confirmLabel: t("ResetPassword"),
    });
    if (!confirmed) {
      return;
    }

    try {
      const result = await reset.mutateAsync(sendLink);
      setTemporary(result.temporaryPassword);
      if (sendLink) {
        notify.success("app.clients.resetLinkSent");
      }
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.clients.tabs.access")}</h2>
          </CardTitle>
          <CardDescription>{t("app.clients.accessHint")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-muted-foreground">{t("Username")}</dt>
              <dd className="font-medium break-words">{account.userName}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("app.clients.activation")}</dt>
              <dd>
                {account.isActivated ? (
                  <Badge variant="secondary">{t("app.clients.activated")}</Badge>
                ) : (
                  <Badge variant="outline">{t("app.clients.notActivated")}</Badge>
                )}
              </dd>
            </div>
          </dl>
          <div className="flex items-center gap-3">
            <Switch
              id="client-sign-in"
              checked={account.canSignIn}
              disabled={!canManage || signIn.isPending}
              onCheckedChange={(on) => void onSignIn(on)}
            />
            <Label htmlFor="client-sign-in">{t("app.clients.canSignIn")}</Label>
          </div>
          <div className="flex flex-wrap gap-2">
            {canManage && !account.isActivated ? (
              <Button
                type="button"
                variant="outline"
                onClick={() => void onInvite()}
                disabled={invite.isPending}
              >
                {t("app.clients.sendInvitation")}
              </Button>
            ) : null}
            {canReset ? (
              <>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => void onReset(true)}
                  disabled={reset.isPending}
                >
                  {t("app.clients.resetLink")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => void onReset(false)}
                  disabled={reset.isPending}
                >
                  {t("app.clients.resetTemporary")}
                </Button>
              </>
            ) : null}
          </div>
          <UserTwoFactorReset userId={account.userId} />
        </CardContent>
      </Card>
      {temporary ? (
        <Alert role="status">
          <AlertTitle>{t("app.clients.temporaryPassword")}</AlertTitle>
          <AlertDescription className="flex flex-col gap-2">
            <code className="w-fit rounded bg-muted px-2 py-1 font-mono text-base select-all">
              {temporary}
            </code>
            <span>{t("app.clients.temporaryPasswordHint")}</span>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              className="self-start"
              onClick={() => setTemporary(null)}
            >
              {t("Close")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
    </div>
  );
}
