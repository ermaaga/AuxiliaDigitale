"use client";

import * as React from "react";
import { PencilIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";

import { Combobox } from "@/components/combobox";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import { timeZones } from "../../labels";
import { updateTenantSchema } from "../../schemas/tenant";
import { updateTenant, useTenantMutation, type TenantDetail } from "../../tenant-api";

/** Name and time zone of the tenant (the identifier and the language never change here). */
export function TenantEditDialog({ tenant }: { tenant: TenantDetail }) {
  const t = useTranslations();
  const notify = useNotify();
  const [open, setOpen] = React.useState(false);
  const zones = React.useMemo(() => timeZones().map((zone) => ({ value: zone, label: zone })), []);
  const form = useZodForm(updateTenantSchema, {
    values: { displayName: tenant.displayName, timeZone: tenant.timeZone },
  });
  const save = useTenantMutation(tenant.slug, (values: { displayName: string; timeZone: string }) =>
    updateTenant(tenant.slug, values),
  );

  const submit = form.handleSubmit((values) =>
    save.mutateAsync(values).then(
      () => {
        notify.success("app.platform.tenant.saved");
        setOpen(false);
      },
      (error: unknown) => {
        applyApiErrors(error, form.setError, ["displayName", "timeZone"]);
      },
    ),
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        save.reset();
      }}
    >
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm">
          <PencilIcon aria-hidden /> {t("Edit")}
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t("Close")}>
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>{t("app.platform.tenant.edit")}</DialogTitle>
            <DialogDescription>{tenant.slug}</DialogDescription>
          </DialogHeader>
          {save.error && !form.formState.errors.displayName && !form.formState.errors.timeZone ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <FormField
            control={form.control}
            name="displayName"
            label={t("app.platform.tenants.name")}
          >
            {(field, props) => <Input {...field} {...props} />}
          </FormField>
          <FormField
            control={form.control}
            name="timeZone"
            label={t("app.platform.tenant.timeZone")}
          >
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
