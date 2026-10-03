"use client";

import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Switch } from "@auxilia/ui/components/switch";
import { Textarea } from "@auxilia/ui/components/textarea";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";

import { useEmployeeSpecializations, useServiceCategories, type Service } from "../api";
import { NONE, serviceSchema, type ServiceOutput } from "../schemas/service";

const FIELDS = [
  "name",
  "description",
  "price",
  "durationDays",
  "categoryId",
  "specializationId",
] as const;

/**
 * The fields of a service (F08, legacy form): name, description, price (euro), duration in days, category and the
 * Employee specialization (kept on create, Q27), active only when editing. Inactive choices stay listed when the
 * service already has them.
 */
export function ServiceForm({
  tenant,
  service,
  disabled,
  submitLabel,
  onSubmit,
  error,
}: {
  tenant: string;
  service?: Service;
  disabled?: boolean;
  submitLabel: string;
  onSubmit: (values: ServiceOutput) => Promise<void>;
  error?: unknown;
}) {
  const t = useTranslations();
  const categories = useServiceCategories(tenant);
  const specializations = useEmployeeSpecializations(tenant);
  const form = useZodForm(serviceSchema, {
    defaultValues: {
      name: service?.name ?? "",
      description: service?.description ?? "",
      price: service ? Number(service.price).toFixed(2) : "",
      durationDays: service ? String(service.durationDays) : "30",
      categoryId: service?.category?.id ?? NONE,
      specializationId: service?.specialization?.id ?? NONE,
      isActive: service?.isActive ?? true,
    },
    disabled,
  });

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values);
    } catch (failure) {
      if (isApiError(failure)) {
        applyApiErrors(failure, form.setError, FIELDS);
      }
    }
  });

  const categoryChoices = (categories.data ?? []).filter(
    (category) => category.isActive || category.id === service?.category?.id,
  );
  const specializationChoices = (specializations.data ?? []).filter(
    (specialization) => specialization.id !== service?.specialization?.id,
  );

  return (
    <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
      {error && !(isApiError(error) && Object.keys(error.fieldErrors).length > 0) ? (
        <ApiErrorAlert error={error} />
      ) : null}
      <FormField control={form.control} name="name" label={`${t("Name")} *`}>
        {(field, props) => <Input {...field} {...props} autoComplete="off" />}
      </FormField>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField control={form.control} name="price" label={`${t("Price")} (€) *`}>
          {(field, props) => <Input {...field} {...props} inputMode="decimal" autoComplete="off" />}
        </FormField>
        <FormField control={form.control} name="durationDays" label={`${t("DurationDays")} *`}>
          {(field, props) => <Input {...field} {...props} inputMode="numeric" autoComplete="off" />}
        </FormField>
        <FormField control={form.control} name="categoryId" label={t("app.services.category")}>
          {(field, props) => (
            <Select value={field.value} onValueChange={field.onChange} disabled={disabled}>
              <SelectTrigger {...props}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE}>{t("None")}</SelectItem>
                {categoryChoices.map((category) => (
                  <SelectItem key={category.id} value={category.id}>
                    {category.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </FormField>
        <FormField control={form.control} name="specializationId" label={t("Specialization")}>
          {(field, props) => (
            <Select value={field.value} onValueChange={field.onChange} disabled={disabled}>
              <SelectTrigger {...props}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE}>{t("None")}</SelectItem>
                {service?.specialization ? (
                  <SelectItem value={service.specialization.id}>
                    {service.specialization.name}
                  </SelectItem>
                ) : null}
                {specializationChoices.map((specialization) => (
                  <SelectItem key={specialization.id} value={specialization.id}>
                    {specialization.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </FormField>
      </div>
      <FormField control={form.control} name="description" label={t("Description")}>
        {(field, props) => <Textarea {...field} {...props} rows={3} />}
      </FormField>
      {service ? (
        <FormField control={form.control} name="isActive" label={t("Status")}>
          {(field, props) => (
            <span className="flex items-center gap-2">
              <Switch
                {...props}
                checked={field.value}
                onCheckedChange={field.onChange}
                disabled={disabled}
              />
              <Label htmlFor={props.id} className="font-normal">
                {field.value ? t("Active") : t("Inactive")}
              </Label>
            </span>
          )}
        </FormField>
      ) : null}
      {disabled ? null : (
        <Button type="submit" className="self-end" disabled={form.formState.isSubmitting}>
          {submitLabel}
        </Button>
      )}
    </form>
  );
}
