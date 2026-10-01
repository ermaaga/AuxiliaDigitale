"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { unwrap } from "@auxilia/api-client";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

import { Combobox } from "@/components/combobox";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { createBffClient } from "@/lib/api/client";
import { PLATFORM_HOME, tenantConsoleHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import { timeZones } from "../labels";
import { CREATE_TENANT_FIELDS, createTenantSchema } from "../schemas/tenant";

/**
 * New tenant (N02): identifier, name, language, time zone and, optionally, the first Administrator. The API creates
 * the tenant at once and provisions it in the background; the page of the tenant shows the progress.
 */
export function CreateTenantForm() {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const zones = React.useMemo(() => timeZones().map((zone) => ({ value: zone, label: zone })), []);
  const form = useZodForm(createTenantSchema, {
    defaultValues: {
      slug: "",
      displayName: "",
      defaultLanguage: "it",
      timeZone: "Europe/Rome",
      inviteAdministrator: true,
      adminEmail: "",
      adminFirstName: "",
      adminLastName: "",
    },
  });
  const [error, setError] = React.useState<unknown>();
  const invite = form.watch("inviteAdministrator");

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const created = unwrap(
        await createBffClient("platform").POST("/api/v1/platform/tenants", {
          body: {
            slug: values.slug,
            displayName: values.displayName,
            defaultLanguage: values.defaultLanguage,
            timeZone: values.timeZone,
            administrator: values.inviteAdministrator
              ? {
                  email: values.adminEmail,
                  firstName: values.adminFirstName,
                  lastName: values.adminLastName,
                }
              : null,
          },
        }),
      );
      notify.success("app.platform.create.created");
      router.push(tenantConsoleHref(created.slug));
      router.refresh();
    } catch (failure) {
      if (!applyApiErrors(failure, form.setError, [], CREATE_TENANT_FIELDS)) {
        setError(failure);
      }
    }
  });

  return (
    <form className="flex max-w-2xl flex-col gap-6" onSubmit={submit} noValidate>
      {error ? <ApiErrorAlert error={error} /> : null}
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField
          control={form.control}
          name="slug"
          label={t("app.platform.tenants.slug")}
          description={t("app.platform.create.slugHint")}
        >
          {(field, props) => (
            <Input
              {...field}
              {...props}
              autoComplete="off"
              spellCheck={false}
              autoCapitalize="none"
            />
          )}
        </FormField>
        <FormField control={form.control} name="displayName" label={t("app.platform.tenants.name")}>
          {(field, props) => <Input {...field} {...props} autoComplete="organization" />}
        </FormField>
        <FormField
          control={form.control}
          name="defaultLanguage"
          label={t("app.platform.tenant.language")}
        >
          {(field, props) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger {...props} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="it">{t("Italian")}</SelectItem>
                <SelectItem value="en">{t("English")}</SelectItem>
              </SelectContent>
            </Select>
          )}
        </FormField>
        <FormField control={form.control} name="timeZone" label={t("app.platform.tenant.timeZone")}>
          {(field, props) => (
            <Combobox
              {...props}
              options={zones}
              value={field.value}
              onChange={(zone) => field.onChange(zone ?? "")}
              placeholder={t("app.platform.tenant.timeZone")}
            />
          )}
        </FormField>
      </div>

      <fieldset className="flex flex-col gap-4 rounded-md border p-4">
        <div className="flex items-start gap-3">
          <Checkbox
            id="invite-admin"
            checked={invite}
            onCheckedChange={(checked) => form.setValue("inviteAdministrator", checked === true)}
            aria-describedby="invite-admin-hint"
          />
          <div className="flex flex-col gap-1">
            <Label htmlFor="invite-admin">{t("app.platform.create.inviteAdmin")}</Label>
            <p id="invite-admin-hint" className="text-sm text-muted-foreground">
              {t("app.platform.create.adminHint")}
            </p>
          </div>
        </div>
        {invite ? (
          <div className="grid gap-4 sm:grid-cols-3">
            <FormField control={form.control} name="adminEmail" label={t("Email")}>
              {(field, props) => <Input {...field} {...props} type="email" autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="adminFirstName" label={t("FirstName")}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="adminLastName" label={t("LastName")}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
          </div>
        ) : null}
      </fieldset>

      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {t("app.platform.create.submit")}
        </Button>
        <Button type="button" variant="outline" asChild>
          <Link href={PLATFORM_HOME}>{t("Cancel")}</Link>
        </Button>
      </div>
    </form>
  );
}
