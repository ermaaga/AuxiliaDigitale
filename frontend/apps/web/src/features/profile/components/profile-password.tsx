"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { PasswordInput, PasswordPolicy } from "@/features/auth";
import { useNotify } from "@/lib/notify";

import { changePassword } from "../api";
import { passwordSchema, type PasswordValues } from "../schemas";

const FIELDS = [
  "currentPassword",
  "newPassword",
  "confirmPassword",
] as const satisfies readonly (keyof PasswordValues)[];

/**
 * Own password change (F04, F35, `POST /me/password`): the current password, the new one twice and the tenant's rules;
 * the API checks the policy and the history and closes the other sessions.
 */
export function ProfilePassword({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const [error, setError] = React.useState<unknown>();
  const form = useZodForm(passwordSchema, {
    defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
  });

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await changePassword(values.currentPassword, values.newPassword);
      form.reset();
      notify.success("app.profile.passwordChanged");
    } catch (failure) {
      if (!applyApiErrors(failure, form.setError, FIELDS, { password: "newPassword" })) {
        setError(failure);
      }
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("ChangePassword")}</h2>
        </CardTitle>
        <CardDescription>{t("app.profile.passwordHint")}</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {error ? <ApiErrorAlert error={error} /> : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2 sm:max-w-[calc(50%-0.5rem)]">
              <FormField control={form.control} name="currentPassword" label={t("CurrentPassword")}>
                {(field, props) => (
                  <PasswordInput {...field} {...props} autoComplete="current-password" />
                )}
              </FormField>
            </div>
            <FormField control={form.control} name="newPassword" label={t("NewPassword")}>
              {(field, props) => (
                <PasswordInput {...field} {...props} autoComplete="new-password" />
              )}
            </FormField>
            <FormField
              control={form.control}
              name="confirmPassword"
              label={t("ConfirmNewPassword")}
            >
              {(field, props) => (
                <PasswordInput {...field} {...props} autoComplete="new-password" />
              )}
            </FormField>
          </div>
          <PasswordPolicy tenant={tenant} id="profile-password-rules" />
          <Button type="submit" className="self-end" disabled={form.formState.isSubmitting}>
            {t("ChangePassword")}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
