"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { CheckIcon } from "lucide-react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import { cn } from "@auxilia/ui/lib/utils";

import { Combobox } from "@/components/combobox";
import { CustomFieldsEditor } from "@/components/custom-fields/custom-fields-editor";
import {
  customFieldText,
  type CustomFieldValues,
} from "@/components/custom-fields/custom-field-value";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useHasRole } from "@/lib/permissions";

import {
  createClient,
  useAssignableEmployees,
  useClientCustomFields,
  useClientMutation,
} from "../api";
import {
  newClientSchema,
  normalizeFiscalCode,
  personBody,
  stepOf,
  WIZARD_STEPS,
  type NewClientValues,
} from "../schemas/client";

const FIELDS = [
  "firstName",
  "lastName",
  "birthDate",
  "fiscalCode",
  "email",
  "phone",
  "employeeUserId",
] as const satisfies readonly (keyof NewClientValues)[];

/**
 * New client (F05, skill auxilia-ui-design "long flows as wizard"): personal data → contacts, employee and custom
 * fields → summary. An Administrator may choose the employee in charge (without one the client goes to the default
 * employee, Q31) and the client gets the activation e-mail (D-06); an Employee's client is assigned to them and cannot
 * sign in until staff enables it (Q60). Server field errors go back to their step.
 */
export function NewClientWizard({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const isAdministrator = useHasRole("Administrator");
  const employees = useAssignableEmployees(tenant, isAdministrator);
  const customFields = useClientCustomFields(tenant);
  const [step, setStep] = React.useState(0);
  const [fields, setFields] = React.useState<CustomFieldValues>({});
  const [fieldErrors, setFieldErrors] = React.useState<Readonly<Record<string, readonly string[]>>>(
    {},
  );
  const create = useClientMutation(tenant, createClient);
  const form = useZodForm(newClientSchema, {
    defaultValues: {
      firstName: "",
      lastName: "",
      birthDate: "",
      fiscalCode: "",
      email: "",
      phone: "",
      employeeUserId: "",
    },
  });
  const steps = [
    t("app.clients.wizard.personal"),
    t("app.clients.wizard.contacts"),
    t("app.clients.wizard.summary"),
  ];
  const headingRef = React.useRef<HTMLHeadingElement>(null);

  const [navigated, setNavigated] = React.useState(false);
  const go = (next: number) => {
    setStep(next);
    setNavigated(true);
  };

  // Screen readers and keyboards start again from the step title (not on the first render).
  React.useEffect(() => {
    if (navigated) {
      headingRef.current?.focus();
    }
  }, [step, navigated]);

  const next = async () => {
    if (await form.trigger([...WIZARD_STEPS[step]!])) {
      go(step + 1);
    }
  };

  const submit = form.handleSubmit(async (values) => {
    setFieldErrors({});
    try {
      const created = await create.mutateAsync({
        ...personBody(values),
        customFields: fields,
        employeeUserId: isAdministrator && values.employeeUserId ? values.employeeUserId : null,
      });
      if (created.invitationSent) {
        notify.success("app.clients.createdInvited");
      } else if (created.invitationErrorCode) {
        notify.info("app.clients.createdNotInvited");
      } else {
        notify.success("app.clients.created");
      }

      router.push(tenantHref(tenant, `/clients/${created.id}`));
    } catch (error) {
      if (isApiError(error)) {
        setFieldErrors(error.fieldErrors);
        applyApiErrors(error, form.setError, FIELDS);
        const first = Object.keys(error.fieldErrors)[0];
        if (first) {
          go(stepOf(first));
        }
      }
    }
  });

  const values = form.watch();
  const employeeName = employees.data?.find(
    (item) => item.userId === values.employeeUserId,
  )?.fullName;
  const summary: [string, string][] = [
    [t("Name"), values.firstName],
    [t("Surname"), values.lastName],
    [t("BirthDate"), values.birthDate],
    [t("app.clients.fiscalCode"), normalizeFiscalCode(values.fiscalCode)],
    [t("Email"), values.email],
    [t("Phone"), values.phone || "—"],
    ...(isAdministrator
      ? [
          [t("app.clients.employee"), employeeName ?? t("app.clients.wizard.defaultEmployee")] as [
            string,
            string,
          ],
        ]
      : []),
    ...customFields.definitions.map(
      (definition) =>
        [definition.label, customFieldText(definition, fields[definition.key]) ?? "—"] as [
          string,
          string,
        ],
    ),
  ];

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      <ol className="flex flex-wrap gap-2" aria-label={t("app.clients.wizard.steps")}>
        {steps.map((label, index) => (
          <li
            key={label}
            aria-current={index === step ? "step" : undefined}
            className={cn(
              "flex items-center gap-2 rounded-full border px-3 py-1 text-sm",
              index === step && "border-primary bg-primary text-primary-foreground",
              index < step && "text-muted-foreground",
            )}
          >
            {index < step ? (
              <CheckIcon aria-hidden className="size-4" />
            ) : (
              <span aria-hidden>{index + 1}</span>
            )}
            {label}
          </li>
        ))}
      </ol>

      <Card>
        <CardHeader>
          <CardTitle>
            <h2 ref={headingRef} tabIndex={-1} className="text-base font-semibold outline-none">
              {t("app.clients.wizard.stepOf", { step: step + 1, total: steps.length })}:{" "}
              {steps[step]}
            </h2>
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {create.error && Object.keys(fieldErrors).length === 0 ? (
            <ApiErrorAlert error={create.error} />
          ) : null}

          <div className={cn("grid gap-4 sm:grid-cols-2", step !== 0 && "hidden")}>
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
                <Input
                  {...field}
                  {...props}
                  autoComplete="off"
                  className="uppercase"
                  maxLength={20}
                />
              )}
            </FormField>
          </div>

          <div className={cn("flex flex-col gap-4", step !== 1 && "hidden")}>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField
                control={form.control}
                name="email"
                label={`${t("Email")} *`}
                description={t("app.clients.wizard.emailIsUserName")}
              >
                {(field, props) => <Input {...field} {...props} type="email" autoComplete="off" />}
              </FormField>
              <FormField control={form.control} name="phone" label={t("Phone")}>
                {(field, props) => <Input {...field} {...props} type="tel" autoComplete="off" />}
              </FormField>
              {isAdministrator ? (
                <FormField
                  control={form.control}
                  name="employeeUserId"
                  label={t("app.clients.employee")}
                  description={t("app.clients.wizard.employeeHint")}
                >
                  {(field, props) => (
                    <Combobox
                      {...props}
                      value={field.value || undefined}
                      onChange={(value) => field.onChange(value ?? "")}
                      loading={employees.isPending}
                      placeholder={t("app.clients.wizard.defaultEmployee")}
                      options={(employees.data ?? []).map((item) => ({
                        value: item.userId,
                        label: item.fullName,
                      }))}
                    />
                  )}
                </FormField>
              ) : null}
            </div>
            {customFields.definitions.length > 0 ? (
              <fieldset className="flex flex-col gap-3">
                <legend className="mb-2 text-sm font-semibold">{t("CustomFields")}</legend>
                <CustomFieldsEditor
                  idPrefix="new-client-cf"
                  definitions={customFields.definitions}
                  values={fields}
                  onChange={setFields}
                  errors={fieldErrors}
                />
              </fieldset>
            ) : null}
          </div>

          {step === 2 ? (
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              {summary.map(([label, value]) => (
                <div key={label}>
                  <dt className="text-muted-foreground">{label}</dt>
                  <dd className="font-medium break-words">{value}</dd>
                </div>
              ))}
              {!isAdministrator ? (
                <p className="text-muted-foreground sm:col-span-2">
                  {t("app.clients.wizard.assignedToYou")}
                </p>
              ) : null}
            </dl>
          ) : null}
        </CardContent>
      </Card>

      <div className="flex flex-wrap justify-between gap-2">
        {step === 0 ? (
          <Button type="button" variant="ghost" asChild>
            <Link href={tenantHref(tenant, "/clients")}>{t("Cancel")}</Link>
          </Button>
        ) : (
          <Button type="button" variant="outline" onClick={() => go(step - 1)}>
            {t("Back")}
          </Button>
        )}
        {/* Separate keys: React must not turn the clicked "Next" button into the submit button (it would submit). */}
        {step < steps.length - 1 ? (
          <Button key="next" type="button" onClick={() => void next()}>
            {t("app.clients.wizard.next")}
          </Button>
        ) : (
          <Button key="create" type="submit" disabled={create.isPending}>
            {t("app.clients.wizard.create")}
          </Button>
        )}
      </div>
    </form>
  );
}
