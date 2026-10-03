"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { CheckCheckIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Textarea } from "@auxilia/ui/components/textarea";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import { completeCase, formatMoney, useCaseMutation, type CaseDetail } from "../api";
import { completeCaseSchema } from "../schemas/case";

/**
 * Completion of a sent case (F09, legacy modal): the amount received now (starts from what is still due), the
 * outcome (rejected) and a note. A completed case never changes again.
 */
export function CompleteCaseDialog({ tenant, value }: { tenant: string; value: CaseDetail }) {
  const t = useTranslations();
  const locale = useLocale();
  const notify = useNotify();
  const [open, setOpen] = React.useState(false);
  const due = Math.max(0, Number(value.price) - Number(value.amountPaid));
  const complete = useCaseMutation(
    tenant,
    (input: { amountPaid: string; rejected: boolean; note: string }) =>
      completeCase(value.id, {
        amountPaid: Number(input.amountPaid),
        rejected: input.rejected,
        note: input.note || null,
      }),
  );
  const form = useZodForm(completeCaseSchema, {
    defaultValues: { amountPaid: due.toFixed(2), rejected: false, note: "" },
  });

  const submit = form.handleSubmit(async (values) => {
    try {
      await complete.mutateAsync(values);
      notify.success(
        values.rejected ? "SubscriptionRejectedSuccess" : "SubscriptionCompletedSuccess",
      );
      setOpen(false);
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, ["note"], { amountPaid: "amountPaid" });
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button">
          <CheckCheckIcon aria-hidden /> {t("app.cases.complete")}
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>{t("app.cases.complete")}</DialogTitle>
          <DialogDescription>
            {t("app.cases.completeDescription", {
              price: formatMoney(value.price, value.currency, locale),
              paid: formatMoney(value.amountPaid, value.currency, locale),
            })}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {complete.error &&
          !(isApiError(complete.error) && Object.keys(complete.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={complete.error} />
          ) : null}
          <FormField
            control={form.control}
            name="amountPaid"
            label={`${t("app.cases.amountReceived")} *`}
          >
            {(field, props) => (
              <Input {...field} {...props} inputMode="decimal" autoComplete="off" />
            )}
          </FormField>
          <FormField control={form.control} name="rejected" label={t("Rejected")}>
            {(field, props) => (
              <span className="flex items-center gap-2">
                <Checkbox
                  {...props}
                  checked={field.value}
                  onCheckedChange={(checked) => field.onChange(checked === true)}
                />
                <Label htmlFor={props.id} className="font-normal">
                  {t("app.cases.rejectedHint")}
                </Label>
              </span>
            )}
          </FormField>
          <FormField control={form.control} name="note" label={t("Notes")}>
            {(field, props) => <Textarea {...field} {...props} rows={2} />}
          </FormField>
          <Button type="submit" className="self-end" disabled={complete.isPending}>
            {t("app.cases.complete")}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
