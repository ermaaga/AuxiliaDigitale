"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import { updateProfile, useProfileMutation, type Profile } from "../api";
import { profileSchema, type ProfileValues } from "../schemas";

const FIELDS = [
  "firstName",
  "lastName",
  "email",
  "phone",
] as const satisfies readonly (keyof ProfileValues)[];

/** The own personal data (F04, legacy profile): first and last name, e-mail, phone; the user name is read-only. */
export function ProfileDataForm({ tenant, profile }: { tenant: string; profile: Profile }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const save = useProfileMutation(tenant, (values: ProfileValues) =>
    updateProfile({ ...values, phone: values.phone || null }),
  );
  const form = useZodForm(profileSchema, {
    defaultValues: {
      firstName: profile.firstName,
      lastName: profile.lastName,
      email: profile.email ?? "",
      phone: profile.phone ?? "",
    },
  });
  const [unmatched, setUnmatched] = React.useState(false);

  const submit = form.handleSubmit(async (values) => {
    setUnmatched(false);
    try {
      await save.mutateAsync(values);
      notify.success("app.profile.saved");
      router.refresh();
    } catch (error) {
      setUnmatched(!applyApiErrors(error, form.setError, FIELDS));
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.profile.data")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {save.error && unmatched ? <ApiErrorAlert error={save.error} /> : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-2 sm:col-span-2">
              <Label htmlFor="profile-user-name">{t("Username")}</Label>
              <Input id="profile-user-name" value={profile.userName} readOnly disabled />
            </div>
            <FormField control={form.control} name="firstName" label={`${t("Name")} *`}>
              {(field, props) => <Input {...field} {...props} autoComplete="given-name" />}
            </FormField>
            <FormField control={form.control} name="lastName" label={`${t("Surname")} *`}>
              {(field, props) => <Input {...field} {...props} autoComplete="family-name" />}
            </FormField>
            <FormField control={form.control} name="email" label={`${t("Email")} *`}>
              {(field, props) => <Input {...field} {...props} type="email" autoComplete="email" />}
            </FormField>
            <FormField control={form.control} name="phone" label={t("Phone")}>
              {(field, props) => <Input {...field} {...props} type="tel" autoComplete="tel" />}
            </FormField>
          </div>
          <Button type="submit" className="self-end" disabled={save.isPending}>
            {t("Save")}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
