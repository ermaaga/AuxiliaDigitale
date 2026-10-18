"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
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
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { PasswordInput } from "@/features/auth/components/password-input";
import { useNotify } from "@/lib/notify";

import {
  connectImap,
  suggestImapServer,
  useConnectedAccountMutation,
  type ConnectedAccount,
} from "../api";
import {
  ADDRESS_MAX,
  HOST_MAX,
  IMAP_SECURITY,
  PASSWORD_MAX,
  USER_NAME_MAX,
  imapSchema,
  securityForPort,
  type ImapValues,
} from "../schemas";

const FIELDS = ["address", "userName", "password", "host", "port", "security"] as const;

/** API codes shown on a field instead of an alert: the server refused the password, or could not be reached. */
const FIELD_CODES: Readonly<Record<string, (typeof FIELDS)[number]>> = {
  "AUX-30012": "host",
  "AUX-30013": "password",
};

/**
 * "Connect PEC / IMAP" (N05): address, user name, password and server; the server of a known provider (Aruba,
 * Legalmail, Poste, Gmail…) is filled in from the domain. The API tests the connection (TLS, public address) before
 * saving. With `account` it reconnects that account (same address, new password).
 */
export function ImapConnectDialog({
  tenant,
  account,
  open,
  onOpenChange,
}: {
  tenant: string;
  account?: ConnectedAccount;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const [known, setKnown] = React.useState(false);
  const form = useZodForm(imapSchema, {
    defaultValues: {
      address: account?.address ?? "",
      userName: account?.address ?? "",
      password: "",
      host: account?.imapHost ?? "",
      port: account?.imapPort ?? 993,
      security: securityForPort(account?.imapPort ?? 993),
    },
  });
  const save = useConnectedAccountMutation(tenant, (values: ImapValues) => connectImap(values));

  const suggest = async (address: string) => {
    const trimmed = address.trim();
    if (!form.getFieldState("userName").isDirty) {
      form.setValue("userName", trimmed);
    }

    if (!trimmed.includes("@") || form.getFieldState("host").isDirty) {
      return;
    }

    try {
      const server = await suggestImapServer(trimmed);
      setKnown(Boolean(server.host));
      if (server.host) {
        form.setValue("host", server.host);
        form.setValue("port", server.port ?? 993);
        form.setValue("security", server.security === "StartTls" ? "StartTls" : "SslOnConnect");
      }
    } catch {
      setKnown(false);
    }
  };

  const submit = form.handleSubmit(async (values) => {
    try {
      await save.mutateAsync(values);
      notify.success("app.connectedAccounts.connected");
      form.reset();
      onOpenChange(false);
    } catch (error) {
      if (!isApiError(error)) {
        return;
      }

      const field = FIELD_CODES[error.errorCode];
      if (field) {
        form.setError(field, { type: "server", message: `errors.${error.errorCode}` });
        save.reset();
      } else if (applyApiErrors(error, form.setError, FIELDS)) {
        save.reset();
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent closeLabel={t("Close")} className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {account
              ? t("app.connectedAccounts.reconnectTitle", { address: account.address })
              : t("app.connectedAccounts.imap")}
          </DialogTitle>
          <DialogDescription>{t("app.connectedAccounts.imapDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {save.error ? <ApiErrorAlert error={save.error} /> : null}
          <FormField
            control={form.control}
            name="address"
            label={`${t("app.connectedAccounts.address")} *`}
          >
            {(field, props) => (
              <Input
                {...props}
                {...field}
                type="email"
                autoComplete="off"
                maxLength={ADDRESS_MAX}
                readOnly={account !== undefined}
                onBlur={() => {
                  field.onBlur();
                  void suggest(field.value);
                }}
              />
            )}
          </FormField>
          <FormField
            control={form.control}
            name="userName"
            label={`${t("app.connectedAccounts.userName")} *`}
            description={t("app.connectedAccounts.userNameHint")}
          >
            {(field, props) => (
              <Input {...props} {...field} autoComplete="off" maxLength={USER_NAME_MAX} />
            )}
          </FormField>
          <FormField control={form.control} name="password" label={`${t("Password")} *`}>
            {(field, props) => (
              <PasswordInput
                {...props}
                {...field}
                autoComplete="new-password"
                maxLength={PASSWORD_MAX}
              />
            )}
          </FormField>
          <FormField
            control={form.control}
            name="host"
            label={`${t("app.connectedAccounts.host")} *`}
            description={known ? t("app.connectedAccounts.serverKnown") : undefined}
          >
            {(field, props) => (
              <Input
                {...props}
                {...field}
                autoComplete="off"
                spellCheck={false}
                maxLength={HOST_MAX}
                placeholder="imap.example.com"
              />
            )}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="port"
              label={`${t("app.connectedAccounts.port")} *`}
            >
              {(field, props) => (
                <Input
                  {...props}
                  {...field}
                  value={String(field.value ?? "")}
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={65535}
                />
              )}
            </FormField>
            <FormField
              control={form.control}
              name="security"
              label={`${t("app.connectedAccounts.security")} *`}
            >
              {(field, props) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger {...props} className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {IMAP_SECURITY.map((security) => (
                      <SelectItem key={security} value={security}>
                        {t(`app.connectedAccounts.securities.${security}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </FormField>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("Cancel")}
            </Button>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending
                ? t("app.connectedAccounts.testing")
                : t("app.connectedAccounts.testAndConnect")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
