"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Switch } from "@auxilia/ui/components/switch";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import { createEmployee, useEmployeeMutation } from "../api";
import { employeeBody, newEmployeeSchema, type NewEmployeeValues } from "../schemas/employee";

const FIELDS = [
  "firstName",
  "lastName",
  "birthDate",
  "fiscalCode",
  "email",
  "phone",
] as const satisfies readonly (keyof NewEmployeeValues)[];

/**
 * New employee (F06, Q55, legacy create form): first and last name, birth date and e-mail (the user name) required,
 * phone and fiscal code optional. With "can sign in" the activation e-mail leaves at once (D-06); then the detail.
 */
export function NewEmployeeForm({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const create = useEmployeeMutation(tenant, createEmployee);
  const [unmatched, setUnmatched] = React.useState(false);
  const form = useZodForm(newEmployeeSchema, {
    defaultValues: {
      firstName: "",
      lastName: "",
      birthDate: "",
      fiscalCode: "",
      email: "",
      phone: "",
      canSignIn: true,
    },
  });

  const submit = form.handleSubmit(async (values) => {
    setUnmatched(false);
    try {
      const created = await create.mutateAsync({
        ...employeeBody(values),
        canSignIn: values.canSignIn,
      });
      if (created.invitationSent) {
        notify.success("app.employees.createdInvited");
      } else if (created.invitationErrorCode) {
        notify.info("app.employees.createdNotInvited");
      } else {
        notify.success("app.employees.created");
      }

      router.push(tenantHref(tenant, `/employees/${created.id}`));
    } catch (error) {
      setUnmatched(!applyApiErrors(error, form.setError, FIELDS));
    }
  });

  return (
    <Card>
      <CardContent>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {create.error && unmatched ? <ApiErrorAlert error={create.error} /> : null}
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
            <FormField
              control={form.control}
              name="email"
              label={`${t("Email")} *`}
              description={t("app.employees.emailIsUserName")}
            >
              {(field, props) => <Input {...field} {...props} type="email" autoComplete="off" />}
            </FormField>
            <FormField control={form.control} name="phone" label={t("Phone")}>
              {(field, props) => <Input {...field} {...props} type="tel" autoComplete="off" />}
            </FormField>
          </div>
          <div className="flex items-start gap-3">
            <Switch
              id="new-employee-sign-in"
              checked={form.watch("canSignIn")}
              onCheckedChange={(on) => form.setValue("canSignIn", on)}
              aria-describedby="new-employee-sign-in-hint"
            />
            <div className="flex flex-col gap-1">
              <Label htmlFor="new-employee-sign-in">{t("app.clients.canSignIn")}</Label>
              <p id="new-employee-sign-in-hint" className="text-sm text-muted-foreground">
                {t("app.employees.signInHint")}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap justify-end gap-2">
            <Button type="button" variant="outline" asChild>
              <Link href={tenantHref(tenant, "/employees")}>{t("Cancel")}</Link>
            </Button>
            <Button type="submit" disabled={create.isPending}>
              {t("CreateNewEmployee")}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
