"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { PlusIcon } from "lucide-react";
import { useTranslations } from "next-intl";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Textarea } from "@auxilia/ui/components/textarea";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useHasRole } from "@/lib/permissions";

import { createRequest, REQUEST_TYPES, useRequestMutation } from "../api";
import { MESSAGE_MAX, requestSchema, SUBJECT_MAX, type RequestOutput } from "../schemas/request";

const FIELDS = ["type", "subject", "message"] as const;

/**
 * A new request (F15, legacy "new request"): type, subject, message; clients choose "ask my operator" (default on,
 * otherwise the office), employees always write to the office. Opens the thread.
 */
export function NewRequestDialog({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const isClient = useHasRole("Client");
  const isEmployee = useHasRole("Employee");
  const [open, setOpen] = React.useState(false);
  const create = useRequestMutation(tenant, (values: RequestOutput) =>
    createRequest({
      type: values.type,
      subject: values.subject,
      message: values.message,
      askMyOperator: isClient && !isEmployee ? values.askMyOperator : null,
    }),
  );
  const form = useZodForm(requestSchema, {
    defaultValues: { type: "General", subject: "", message: "", askMyOperator: true },
  });

  const submit = form.handleSubmit(async (values) => {
    try {
      const created = await create.mutateAsync(values);
      notify.success("app.requests.sent");
      setOpen(false);
      form.reset();
      router.push(tenantHref(tenant, `/requests/${created.id}`));
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
          <PlusIcon aria-hidden /> {t("NewRequest")}
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>{t("NewRequest")}</DialogTitle>
          <DialogDescription>
            {isClient && !isEmployee
              ? t("app.requests.newDescriptionClient")
              : t("app.requests.newDescriptionStaff")}
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {create.error &&
          !(isApiError(create.error) && Object.keys(create.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={create.error} />
          ) : null}
          {isClient && !isEmployee ? (
            <FormField
              control={form.control}
              name="askMyOperator"
              label={t("app.requests.recipient")}
            >
              {(field, props) => (
                <span className="flex items-center gap-2">
                  <Checkbox
                    {...props}
                    checked={field.value}
                    onCheckedChange={(value) => field.onChange(value === true)}
                  />
                  <Label htmlFor={props.id} className="font-normal">
                    {t("app.requests.askMyOperator")}
                  </Label>
                </span>
              )}
            </FormField>
          ) : null}
          <FormField control={form.control} name="type" label={`${t("RequestType")} *`}>
            {(field, props) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger {...props}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {REQUEST_TYPES.map((type) => (
                    <SelectItem key={type} value={type}>
                      {t(`app.requests.type.${type}` as "app.requests.type.General")}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </FormField>
          <FormField control={form.control} name="subject" label={`${t("Subject")} *`}>
            {(field, props) => (
              <Input {...field} {...props} maxLength={SUBJECT_MAX} autoComplete="off" />
            )}
          </FormField>
          <FormField control={form.control} name="message" label={`${t("Message")} *`}>
            {(field, props) => <Textarea {...field} {...props} rows={5} maxLength={MESSAGE_MAX} />}
          </FormField>
          <Button type="submit" className="self-end" disabled={form.formState.isSubmitting}>
            {t("Send")}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
