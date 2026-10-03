"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";

import { CustomFieldsEditor } from "@/components/custom-fields/custom-fields-editor";
import type { CustomFieldValues } from "@/components/custom-fields/custom-field-value";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import { updateClient, useClientCustomFields, useClientMutation, type ClientDetail } from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";
import { editClientSchema, personBody, type EditClientValues } from "../schemas/client";
import { customFieldValues } from "./clients-table";

const FIELDS = [
  "firstName",
  "lastName",
  "birthDate",
  "fiscalCode",
  "email",
  "phone",
  "userName",
] as const satisfies readonly (keyof EditClientValues)[];

/**
 * Personal data, user name (unique, Q52) and custom fields of a client (F05, legacy detail "info" card); read-only
 * without `directory.clients.manage`.
 */
export function ClientDataForm({ tenant, client }: { tenant: string; client: ClientDetail }) {
  const t = useTranslations();
  const notify = useNotify();
  const canEdit = useCan(DIRECTORY_PERMISSIONS.manageClients);
  const customFields = useClientCustomFields(tenant);
  const [fields, setFields] = React.useState<CustomFieldValues>(() =>
    customFieldValues(client.customFields),
  );
  const [fieldErrors, setFieldErrors] = React.useState<Readonly<Record<string, readonly string[]>>>(
    {},
  );
  const save = useClientMutation(tenant, (values: EditClientValues) =>
    updateClient(client.id, {
      ...personBody(values),
      userName: values.userName,
      customFields: fields,
    }),
  );
  const form = useZodForm(editClientSchema, {
    defaultValues: {
      firstName: client.firstName,
      lastName: client.lastName,
      birthDate: client.birthDate ?? "",
      fiscalCode: client.fiscalCode ?? "",
      email: client.email ?? "",
      phone: client.phone ?? "",
      userName: client.account?.userName ?? client.email ?? "",
    },
    disabled: !canEdit,
  });

  const submit = form.handleSubmit(async (values) => {
    setFieldErrors({});
    try {
      await save.mutateAsync(values);
      notify.success("app.clients.saved");
    } catch (error) {
      if (isApiError(error)) {
        setFieldErrors(error.fieldErrors);
        applyApiErrors(error, form.setError, FIELDS);
      }
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.clients.tabs.data")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {save.error && Object.keys(fieldErrors).length === 0 ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField control={form.control} name="firstName" label={`${t("Name")} *`}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="lastName" label={`${t("Surname")} *`}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="birthDate" label={`${t("BirthDate")} *`}>
              {(field, props) => <Input {...field} {...props} type="date" />}
            </FormField>
            <FormField
              control={form.control}
              name="fiscalCode"
              label={`${t("app.clients.fiscalCode")} *`}
            >
              {(field, props) => (
                <Input {...field} {...props} autoComplete="off" className="uppercase" />
              )}
            </FormField>
            <FormField control={form.control} name="email" label={`${t("Email")} *`}>
              {(field, props) => <Input {...field} {...props} type="email" autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="phone" label={t("Phone")}>
              {(field, props) => <Input {...field} {...props} type="tel" autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="userName" label={`${t("Username")} *`}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
          </div>
          {customFields.definitions.length > 0 ? (
            <fieldset className="flex flex-col gap-3" disabled={!canEdit}>
              <legend className="mb-2 text-sm font-semibold">{t("CustomFields")}</legend>
              <CustomFieldsEditor
                idPrefix="client-cf"
                definitions={customFields.definitions}
                values={fields}
                onChange={setFields}
                errors={fieldErrors}
              />
            </fieldset>
          ) : null}
          {canEdit ? (
            <Button type="submit" className="self-end" disabled={save.isPending}>
              {t("Save")}
            </Button>
          ) : null}
        </form>
      </CardContent>
    </Card>
  );
}
