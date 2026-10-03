"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { PlusIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";

import { Combobox } from "@/components/combobox";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useAssignableEmployees, useClients } from "@/features/clients";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useHasRole } from "@/lib/permissions";

import { formatMoney, openCase, useActiveServices, useCaseMutation } from "../api";
import { newCaseSchema, today, type NewCaseValues } from "../schemas/case";

const FIELDS = ["clientId", "serviceId", "startedOn", "dueOn", "employeeUserId"] as const;

/**
 * Quick creation of a case (F09, legacy panel): client (fixed in the client 360°), active service shown as
 * "name - price (days)", start date (today), optional due date (D-07) and, for Administrators, the employee to hand
 * the client to. Opens the new case.
 */
export function NewCaseDialog({ tenant, clientId }: { tenant: string; clientId?: string }) {
  const t = useTranslations();
  const locale = useLocale();
  const router = useRouter();
  const notify = useNotify();
  const isAdministrator = useHasRole("Administrator");
  const [open, setOpen] = React.useState(false);
  const [clientSearch, setClientSearch] = React.useState("");
  const [serviceSearch, setServiceSearch] = React.useState("");
  const clients = useClients(tenant, {
    view: "all",
    page: 1,
    pageSize: 20,
    "filter[fullName]": clientSearch || undefined,
  });
  const services = useActiveServices(tenant, serviceSearch, open);
  const employees = useAssignableEmployees(tenant, open && isAdministrator);
  const create = useCaseMutation(tenant, (values: NewCaseValues) =>
    openCase({
      clientId: values.clientId,
      serviceId: values.serviceId,
      startedOn: values.startedOn,
      dueOn: values.dueOn || null,
      employeeUserId: isAdministrator && values.employeeUserId ? values.employeeUserId : null,
      specializationId: null,
      customFields: null,
    }),
  );
  const form = useZodForm(newCaseSchema, {
    defaultValues: {
      clientId: clientId ?? "",
      serviceId: "",
      startedOn: today(),
      dueOn: "",
      employeeUserId: "",
    },
  });

  const submit = form.handleSubmit(async (values) => {
    try {
      const created = await create.mutateAsync(values);
      notify.success("app.cases.opened");
      setOpen(false);
      router.push(tenantHref(tenant, `/cases/${created.id}`));
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, FIELDS);
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button">
          <PlusIcon aria-hidden /> {t("AddSubscription")}
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>{t("AddSubscription")}</DialogTitle>
          <DialogDescription>{t("app.cases.newDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {create.error &&
          !(isApiError(create.error) && Object.keys(create.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={create.error} />
          ) : null}
          {clientId === undefined ? (
            <FormField control={form.control} name="clientId" label={`${t("Client")} *`}>
              {(field, props) => (
                <Combobox
                  {...props}
                  options={(clients.data?.items ?? []).map((client) => ({
                    value: client.id,
                    label: `${client.lastName} ${client.firstName}`,
                    description: client.email ?? undefined,
                  }))}
                  value={field.value || undefined}
                  onChange={(value) => field.onChange(value ?? "")}
                  placeholder={t("app.documents.chooseClient")}
                  onSearch={setClientSearch}
                  loading={clients.isFetching}
                />
              )}
            </FormField>
          ) : null}
          <FormField control={form.control} name="serviceId" label={`${t("Membership")} *`}>
            {(field, props) => (
              <Combobox
                {...props}
                options={(services.data?.items ?? []).map((service) => ({
                  value: service.id,
                  label: `${service.name} - ${formatMoney(service.price, service.currency, locale)} (${t("app.cases.days", { days: Number(service.durationDays) })})`,
                  description: service.category?.name,
                }))}
                value={field.value || undefined}
                onChange={(value) => field.onChange(value ?? "")}
                placeholder={t("app.cases.chooseService")}
                onSearch={setServiceSearch}
                loading={services.isFetching}
              />
            )}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField control={form.control} name="startedOn" label={`${t("StartDate")} *`}>
              {(field, props) => <Input {...field} {...props} type="date" />}
            </FormField>
            <FormField control={form.control} name="dueOn" label={t("app.cases.dueOn")}>
              {(field, props) => <Input {...field} {...props} type="date" />}
            </FormField>
          </div>
          {isAdministrator ? (
            <FormField
              control={form.control}
              name="employeeUserId"
              label={t("Employee")}
              description={t("app.cases.employeeHint")}
            >
              {(field, props) => (
                <Combobox
                  {...props}
                  options={(employees.data ?? []).map((employee) => ({
                    value: employee.userId,
                    label: employee.fullName,
                  }))}
                  value={field.value || undefined}
                  onChange={(value) => field.onChange(value ?? "")}
                  placeholder={t("app.cases.defaultEmployee")}
                  loading={employees.isFetching}
                />
              )}
            </FormField>
          ) : null}
          <Button type="submit" className="self-end" disabled={create.isPending}>
            {t("Create")}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
