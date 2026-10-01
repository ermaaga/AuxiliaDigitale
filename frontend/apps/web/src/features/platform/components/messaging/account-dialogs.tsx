"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import {
  createSmtpAccount,
  sendTestMessage,
  smtpSettingsOf,
  updateAccount,
  useMessagingMutation,
  type MessagingAccount,
} from "../../messaging-api";
import {
  smtpAccountSchema,
  testMessageSchema,
  type SmtpAccountValues,
} from "../../schemas/messaging";

const SECURITY = ["None", "StartTls", "SslOnConnect"] as const;

/**
 * New or edited SMTP account (N03). The password is write-only: the API never returns it, an empty field keeps the
 * stored one.
 */
export function SmtpAccountDialog({
  slug,
  account,
  onClose,
}: {
  slug: string;
  /** Undefined: a new account. */
  account?: MessagingAccount;
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const settings = smtpSettingsOf(account);
  const form = useZodForm(smtpAccountSchema, {
    defaultValues: {
      name: account?.name ?? "",
      host: settings.host,
      port: String(settings.port),
      security: settings.security,
      username: settings.username ?? "",
      password: "",
      fromAddress: settings.fromAddress,
      fromName: settings.fromName ?? "",
    },
  });
  const save = useMessagingMutation(slug, async (values: SmtpAccountValues): Promise<void> => {
    const body = {
      name: values.name,
      settings: {
        host: values.host,
        port: Number(values.port),
        security: values.security,
        username: values.username || null,
        fromAddress: values.fromAddress,
        fromName: values.fromName || null,
      },
      secret: values.password || null,
    };
    if (account) {
      await updateAccount(slug, account.id, body);
    } else {
      await createSmtpAccount(slug, body);
    }
  });

  const submit = form.handleSubmit((values) =>
    save.mutateAsync(values).then(
      () => {
        notify.success("app.platform.messaging.accountSaved");
        onClose();
      },
      (error: unknown) => {
        // The API answers `settings` for any invalid SMTP field: the host is the usual culprit.
        applyApiErrors(error, form.setError, ["name"], { settings: "host" });
      },
    ),
  );

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")} className="sm:max-w-lg">
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>
              {account
                ? t("app.platform.messaging.editAccount")
                : t("app.platform.messaging.newAccount")}
            </DialogTitle>
            <DialogDescription>{t("app.platform.messaging.smtpDescription")}</DialogDescription>
          </DialogHeader>
          {save.error && Object.keys(form.formState.errors).length === 0 ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <FormField
            control={form.control}
            name="name"
            label={t("app.platform.messaging.accountName")}
          >
            {(field, props) => <Input {...field} {...props} maxLength={100} />}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-[1fr_7rem]">
            <FormField control={form.control} name="host" label={t("app.platform.messaging.host")}>
              {(field, props) => (
                <Input {...field} {...props} autoComplete="off" spellCheck={false} />
              )}
            </FormField>
            <FormField control={form.control} name="port" label={t("app.platform.messaging.port")}>
              {(field, props) => <Input {...field} {...props} inputMode="numeric" />}
            </FormField>
          </div>
          <FormField
            control={form.control}
            name="security"
            label={t("app.platform.messaging.security")}
          >
            {(field, props) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger {...props} className="w-full" onBlur={field.onBlur} ref={field.ref}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SECURITY.map((value) => (
                    <SelectItem key={value} value={value}>
                      {t(`app.platform.messaging.securityOption.${value}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField control={form.control} name="username" label={t("Username")}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
            <FormField
              control={form.control}
              name="password"
              label={t("Password")}
              description={
                account?.hasSecret ? t("app.platform.messaging.passwordKept") : undefined
              }
            >
              {(field, props) => (
                <Input {...field} {...props} type="password" autoComplete="new-password" />
              )}
            </FormField>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="fromAddress"
              label={t("app.platform.messaging.fromAddress")}
            >
              {(field, props) => <Input {...field} {...props} type="email" autoComplete="off" />}
            </FormField>
            <FormField
              control={form.control}
              name="fromName"
              label={t("app.platform.messaging.fromName")}
            >
              {(field, props) => <Input {...field} {...props} />}
            </FormField>
          </div>
          <DialogFooter closeLabel={t("Cancel")}>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Sends a test message now; the outcome (sent or the reason it was not) stays in the dialog. */
export function TestMessageDialog({
  slug,
  account,
  onClose,
}: {
  slug: string;
  account: MessagingAccount;
  onClose: () => void;
}) {
  const t = useTranslations();
  const form = useZodForm(testMessageSchema, { defaultValues: { recipient: "", language: "it" } });
  const send = useMessagingMutation(slug, (values: { recipient: string; language: string }) =>
    sendTestMessage(slug, account.id, values),
  );
  const outcome = send.data;

  const submit = form.handleSubmit((values) =>
    send.mutateAsync(values).catch((error: unknown) => {
      applyApiErrors(error, form.setError, ["recipient"], { recipient: "recipient" });
    }),
  );

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")}>
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>
              {t("app.platform.messaging.testTitle", { account: account.name })}
            </DialogTitle>
            <DialogDescription>{t("app.platform.messaging.testDescription")}</DialogDescription>
          </DialogHeader>
          {outcome ? (
            <Alert variant={outcome.sent ? "default" : "destructive"} role="status">
              <AlertDescription>
                {outcome.sent
                  ? t("app.platform.messaging.testSent")
                  : t("app.platform.messaging.testFailed", {
                      reason: t.has(`errors.${outcome.errorCode}`)
                        ? t(`errors.${outcome.errorCode}`)
                        : (outcome.errorCode ?? ""),
                    })}{" "}
                {outcome.errorCode ? <code>{outcome.errorCode}</code> : null}
              </AlertDescription>
            </Alert>
          ) : null}
          {send.error && !form.formState.errors.recipient ? (
            <ApiErrorAlert error={send.error} />
          ) : null}
          <FormField
            control={form.control}
            name="recipient"
            label={t("app.platform.messaging.recipient")}
          >
            {(field, props) => <Input {...field} {...props} type="email" autoComplete="email" />}
          </FormField>
          <FormField control={form.control} name="language" label={t("Language")}>
            {(field, props) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger {...props} className="w-full" onBlur={field.onBlur} ref={field.ref}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="it">{t("languages.it")}</SelectItem>
                  <SelectItem value="en">{t("languages.en")}</SelectItem>
                </SelectContent>
              </Select>
            )}
          </FormField>
          <DialogFooter closeLabel={t("Close")}>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {t("app.platform.messaging.sendTest")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
