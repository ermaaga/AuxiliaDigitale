"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import { updateEmployee, useEmployeeMutation, type EmployeeDetail } from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";
import { editEmployeeSchema, employeeBody, type EditEmployeeValues } from "../schemas/employee";

const FIELDS = [
  "firstName",
  "lastName",
  "birthDate",
  "fiscalCode",
  "email",
  "phone",
  "userName",
] as const satisfies readonly (keyof EditEmployeeValues)[];

/** Personal data and user name (unique, Q52) of an employee (F06, legacy detail); read-only without manage permission. */
export function EmployeeDataForm({
  tenant,
  employee,
}: {
  tenant: string;
  employee: EmployeeDetail;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const canEdit = useCan(EMPLOYEE_PERMISSIONS.manage);
  const [unmatched, setUnmatched] = React.useState(false);
  const save = useEmployeeMutation(tenant, (values: EditEmployeeValues) =>
    updateEmployee(employee.id, { ...employeeBody(values), userName: values.userName }),
  );
  const form = useZodForm(editEmployeeSchema, {
    defaultValues: {
      firstName: employee.firstName,
      lastName: employee.lastName,
      birthDate: employee.birthDate ?? "",
      fiscalCode: employee.fiscalCode ?? "",
      email: employee.email ?? "",
      phone: employee.phone ?? "",
      userName: employee.userName,
    },
    disabled: !canEdit,
  });

  const submit = form.handleSubmit(async (values) => {
    setUnmatched(false);
    try {
      await save.mutateAsync(values);
      notify.success("app.clients.saved");
    } catch (error) {
      setUnmatched(!applyApiErrors(error, form.setError, FIELDS));
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
          {save.error && unmatched ? <ApiErrorAlert error={save.error} /> : null}
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
            <FormField control={form.control} name="fiscalCode" label={t("app.clients.fiscalCode")}>
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
