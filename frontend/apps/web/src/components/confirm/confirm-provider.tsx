"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@auxilia/ui/components/alert-dialog";
import { buttonVariants } from "@auxilia/ui/components/button";

export type ConfirmOptions = {
  /** Translated texts; the title defaults to "Are you sure?". */
  title?: string;
  description: string;
  confirmLabel?: string;
  /** `destructive` for deletions (legacy danger style). */
  variant?: "default" | "destructive";
};

type Pending = ConfirmOptions & { resolve: (confirmed: boolean) => void };

const ConfirmContext = React.createContext<((options: ConfirmOptions) => Promise<boolean>) | null>(
  null,
);

/**
 * Confirmation of destructive actions (F34, legacy `ConfirmationService`): `const ok = await confirm({ ... })`.
 * Focus goes to Cancel, Esc cancels, focus returns to the trigger.
 */
export function ConfirmProvider({ children }: { children: React.ReactNode }) {
  const t = useTranslations();
  const [pending, setPending] = React.useState<Pending | null>(null);

  const confirm = React.useCallback(
    (options: ConfirmOptions) =>
      new Promise<boolean>((resolve) => setPending({ ...options, resolve })),
    [],
  );

  const close = (confirmed: boolean) => {
    pending?.resolve(confirmed);
    setPending(null);
  };

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <AlertDialog open={pending !== null} onOpenChange={(open) => !open && close(false)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{pending?.title ?? t("common.confirm.title")}</AlertDialogTitle>
            <AlertDialogDescription>{pending?.description}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={() => close(false)}>{t("Cancel")}</AlertDialogCancel>
            <AlertDialogAction
              className={
                pending?.variant === "destructive"
                  ? buttonVariants({ variant: "destructive" })
                  : undefined
              }
              onClick={() => close(true)}
            >
              {pending?.confirmLabel ?? t("Confirm")}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </ConfirmContext.Provider>
  );
}

export function useConfirm(): (options: ConfirmOptions) => Promise<boolean> {
  const confirm = React.useContext(ConfirmContext);
  if (!confirm) {
    throw new Error("useConfirm needs a ConfirmProvider.");
  }

  return confirm;
}
