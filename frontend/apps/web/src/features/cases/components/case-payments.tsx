"use client";

import { isApiError } from "@auxilia/api-client";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@auxilia/ui/components/table";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import { addCasePayment, formatMoney, useCaseMutation, type CaseDetail } from "../api";
import { paymentSchema, today } from "../schemas/case";

/** Price, money received and balance due of a case (Q02), its payments and, before completion, a new payment. */
export function CasePayments({ tenant, value }: { tenant: string; value: CaseDetail }) {
  const t = useTranslations();
  const locale = useLocale();
  const format = useFormatter();
  const notify = useNotify();
  const money = (amount: number | string) => formatMoney(amount, value.currency, locale);
  const balance = Math.max(0, Number(value.price) - Number(value.amountPaid));
  const canAdd = value.canManage && value.status !== "Completed";
  const add = useCaseMutation(tenant, (input: { amount: string; paidOn: string; note: string }) =>
    addCasePayment(value.id, {
      amount: Number(input.amount),
      paidOn: input.paidOn,
      note: input.note || null,
    }),
  );
  const form = useZodForm(paymentSchema, {
    defaultValues: { amount: "", paidOn: today(), note: "" },
  });

  const submit = form.handleSubmit(async (values) => {
    try {
      await add.mutateAsync(values);
      notify.success("app.cases.paymentRecorded");
      form.reset({ amount: "", paidOn: today(), note: "" });
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, ["amount", "paidOn", "note"]);
      }
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.cases.payments")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <dl className="grid grid-cols-3 gap-2 text-sm">
          <div>
            <dt className="text-muted-foreground">{t("Price")}</dt>
            <dd className="font-medium">{money(value.price)}</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">{t("AmountPaid")}</dt>
            <dd className="font-medium">{money(value.amountPaid)}</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">{t("app.cases.balance")}</dt>
            <dd className="font-medium">{money(balance)}</dd>
          </div>
        </dl>
        {value.payments.length > 0 ? (
          <Table aria-label={t("app.cases.payments")}>
            <TableHeader>
              <TableRow>
                <TableHead>{t("app.cases.paidOn")}</TableHead>
                <TableHead>{t("Amount")}</TableHead>
                <TableHead>{t("Notes")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {value.payments.map((payment) => (
                <TableRow key={payment.id}>
                  <TableCell>
                    {format.dateTime(new Date(payment.paidOn), { dateStyle: "medium" })}
                  </TableCell>
                  <TableCell>{money(payment.amount)}</TableCell>
                  <TableCell>{payment.note ?? "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <p className="text-sm text-muted-foreground">{t("app.cases.noPayments")}</p>
        )}
        {canAdd ? (
          <form
            onSubmit={(event) => void submit(event)}
            noValidate
            className="grid gap-3 sm:grid-cols-[1fr_1fr_2fr_auto] sm:items-end"
          >
            {add.error &&
            !(isApiError(add.error) && Object.keys(add.error.fieldErrors).length > 0) ? (
              <div className="sm:col-span-4">
                <ApiErrorAlert error={add.error} />
              </div>
            ) : null}
            <FormField control={form.control} name="amount" label={`${t("Amount")} *`}>
              {(field, props) => (
                <Input {...field} {...props} inputMode="decimal" autoComplete="off" />
              )}
            </FormField>
            <FormField control={form.control} name="paidOn" label={`${t("app.cases.paidOn")} *`}>
              {(field, props) => <Input {...field} {...props} type="date" />}
            </FormField>
            <FormField control={form.control} name="note" label={t("Notes")}>
              {(field, props) => <Input {...field} {...props} autoComplete="off" />}
            </FormField>
            <Button type="submit" disabled={add.isPending}>
              {t("app.cases.addPayment")}
            </Button>
          </form>
        ) : null}
      </CardContent>
    </Card>
  );
}
