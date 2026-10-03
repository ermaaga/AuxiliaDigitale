import { z } from "zod";

/** Server limits (`Case` in the API, F09). */
export const NOTE_MAX = 500;

/** Two decimals, zero or more (the amount received at completion) or more than zero (a payment). */
const amount = (allowZero: boolean) =>
  z
    .string()
    .trim()
    .transform((value) => value.replace(",", "."))
    .refine(
      (value) => /^\d+(\.\d{1,2})?$/.test(value) && (allowZero || Number(value) > 0),
      "validation.cases.amount",
    );

/** A quick new case (legacy panel): client, service, start date, optional due date and employee. */
export const newCaseSchema = z
  .object({
    clientId: z.string().min(1, "validation.cases.client"),
    serviceId: z.string().min(1, "validation.cases.service"),
    startedOn: z.string().min(1, "validation.cases.startedOn"),
    dueOn: z.string(),
    employeeUserId: z.string(),
  })
  .refine((values) => !values.dueOn || values.dueOn >= values.startedOn, {
    path: ["dueOn"],
    message: "validation.cases.dueOn",
  });

export type NewCaseValues = z.infer<typeof newCaseSchema>;

/** Completion (legacy modal): amount received (default price minus what was paid), outcome, note. */
export const completeCaseSchema = z.object({
  amountPaid: amount(true),
  rejected: z.boolean(),
  note: z.string().max(NOTE_MAX, "validation.cases.note"),
});

export type CompleteCaseValues = z.input<typeof completeCaseSchema>;

/** A payment received before completion. */
export const paymentSchema = z.object({
  amount: amount(false),
  paidOn: z.string().min(1, "validation.cases.paidOn"),
  note: z.string().max(NOTE_MAX, "validation.cases.note"),
});

export type PaymentValues = z.input<typeof paymentSchema>;

/** Today in the browser's time zone as `yyyy-MM-dd` (date inputs). */
export function today(now = new Date()): string {
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 10);
}
