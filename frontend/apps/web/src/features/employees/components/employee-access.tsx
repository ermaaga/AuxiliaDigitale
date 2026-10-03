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
import { useCan } from "@/lib/permissions";

import {
  resetEmployeePassword,
  sendEmployeeInvitation,
  setEmployeeSignIn,
  useEmployeeMutation,
  type EmployeeDetail,
} from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";

/**
 * The sign-in account of an employee (F06, D-06): sign-in on/off (legacy "active"; the default employee stays enabled,
 * `AUX-13030`), activation e-mail while there is no password, and the password reset by staff (link, or a temporary
 * password shown once).
 */
export function EmployeeAccess({ tenant, employee }: { tenant: string; employee: EmployeeDetail }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(EMPLOYEE_PERMISSIONS.manage);
  const [temporary, setTemporary] = React.useState<string | null>(null);
  const signIn = useEmployeeMutation(tenant, (on: boolean) => setEmployeeSignIn(employee.id, on));
  const invite = useEmployeeMutation(tenant, () => sendEmployeeInvitation(employee.id));
  const reset = useEmployeeMutation(tenant, (sendLink: boolean) =>
    resetEmployeePassword(employee.id, sendLink),
  );

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
        sendLink ? "app.employees.resetLinkConfirm" : "app.employees.resetTemporaryConfirm",
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
          <CardDescription>{t("app.employees.accessHint")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-muted-foreground">{t("Username")}</dt>
              <dd className="font-medium break-words">{employee.userName}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("app.clients.activation")}</dt>
              <dd>
                {employee.isActivated ? (
                  <Badge variant="secondary">{t("app.clients.activated")}</Badge>
                ) : (
                  <Badge variant="outline">{t("app.clients.notActivated")}</Badge>
                )}
              </dd>
            </div>
          </dl>
          <div className="flex items-center gap-3">
            <Switch
              id="employee-sign-in"
              checked={employee.canSignIn}
              disabled={
                !canManage || signIn.isPending || (employee.isDefault && employee.canSignIn)
              }
              onCheckedChange={(on) => void onSignIn(on)}
            />
            <Label htmlFor="employee-sign-in">{t("app.clients.canSignIn")}</Label>
          </div>
          <div className="flex flex-wrap gap-2">
            {canManage && !employee.isActivated ? (
              <Button
                type="button"
                variant="outline"
                onClick={() => void onInvite()}
                disabled={invite.isPending}
              >
                {t("app.clients.sendInvitation")}
              </Button>
            ) : null}
            {canManage ? (
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
        </CardContent>
      </Card>
      {temporary ? (
        <Alert role="status">
          <AlertTitle>{t("app.clients.temporaryPassword")}</AlertTitle>
          <AlertDescription className="flex flex-col gap-2">
            <code className="w-fit rounded bg-muted px-2 py-1 font-mono text-base select-all">
              {temporary}
            </code>
            <span>{t("app.employees.temporaryPasswordHint")}</span>
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
