"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import { resetUserTwoFactor } from "../api";

/**
 * "Reset authenticator app" on the access tab of an employee or a client (N04, lost phone): an Administrator removes
 * the app; the user's sessions end and the app is enrolled again. Hidden without `identity.users.manage`.
 */
export function UserTwoFactorReset({ userId }: { userId: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan("identity.users.manage");
  const [busy, setBusy] = React.useState(false);

  if (!canManage) {
    return null;
  }

  const onReset = async () => {
    const confirmed = await confirm({
      title: t("app.twoFactor.resetTitle"),
      description: t("app.twoFactor.resetText"),
      confirmLabel: t("app.twoFactor.reset"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    setBusy(true);
    try {
      await resetUserTwoFactor(userId);
      notify.success("app.twoFactor.resetDone");
    } catch (error) {
      notify.error(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-2 border-t pt-4">
      <p className="text-sm text-muted-foreground">{t("app.twoFactor.resetHint")}</p>
      <Button
        type="button"
        variant="outline"
        className="self-start"
        disabled={busy}
        onClick={() => void onReset()}
      >
        {t("app.twoFactor.reset")}
      </Button>
    </div>
  );
}
