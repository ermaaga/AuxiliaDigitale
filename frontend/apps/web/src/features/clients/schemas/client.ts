import { z } from "zod";

/** Mirrors `Person` and `ClientRules` (domain, Q53, Q54); the API stays the authority (fiscal code unique, Q54). */
export const NAME_MAX = 100;
export const EMAIL_MAX = 256;

/** Q54: 16 characters with the month letter; omocodia replaces digits with L–V. */
export const FISCAL_CODE =
  /^[A-Z]{6}[0-9LMNPQRSTUV]{2}[ABCDEHLMPRST][0-9LMNPQRSTUV]{2}[A-Z][0-9LMNPQRSTUV]{3}[A-Z]$/;

/** Q53: one phone rule for staff forms, registration and import. */
export const PHONE = /^[\d\s+\-()]{8,20}$/;

const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

/** Upper case without spaces, as the API stores it. */
export function normalizeFiscalCode(value: string): string {
  return value.replaceAll(" ", "").toUpperCase();
}

/** `YYYY-MM-DD` of today in the browser's calendar. */
function today(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const name = (key: string) => z.string().trim().min(1, key).max(NAME_MAX, key);

/** The personal data every client needs (legacy staff form). */
export const clientSchema = z.object({
  firstName: name("validation.person.firstName"),
  lastName: name("validation.person.lastName"),
  birthDate: z
    .string()
    .refine(
      (value) => /^\d{4}-\d{2}-\d{2}$/.test(value) && value >= "1900-01-01" && value <= today(),
      "validation.person.birthDate",
    ),
  email: z
    .string()
    .trim()
    .max(EMAIL_MAX, "validation.person.email")
    .refine((value) => EMAIL.test(value), "validation.person.email"),
  phone: z
    .string()
    .trim()
    .refine((value) => value === "" || PHONE.test(value), "validation.person.phone"),
  fiscalCode: z
    .string()
    .refine(
      (value) => FISCAL_CODE.test(normalizeFiscalCode(value)),
      "validation.person.fiscalCode",
    ),
});

export type ClientValues = z.output<typeof clientSchema>;

/** The new client wizard: the personal data plus the employee in charge (Administrators only). */
export const newClientSchema = clientSchema.extend({ employeeUserId: z.string() });

export type NewClientValues = z.output<typeof newClientSchema>;

/** Editing a client: the user name (unique, Q52) changes too. */
export const editClientSchema = clientSchema.extend({
  userName: z
    .string()
    .trim()
    .min(1, "validation.user.userName")
    .max(EMAIL_MAX, "validation.user.userName")
    .refine((value) => !/\s/.test(value), "validation.user.userName"),
});

export type EditClientValues = z.output<typeof editClientSchema>;

/** The fields of each wizard step, validated before going on. */
export const WIZARD_STEPS = [
  ["firstName", "lastName", "birthDate", "fiscalCode"],
  ["email", "phone", "employeeUserId"],
  [],
] as const satisfies readonly (readonly (keyof NewClientValues)[])[];

/** The step holding a field (the first one when unknown, e.g. custom fields are on step 2). */
export function stepOf(field: string): number {
  if (field.startsWith("customFields.")) {
    return 1;
  }

  const index = WIZARD_STEPS.findIndex((fields) => (fields as readonly string[]).includes(field));
  return index < 0 ? 0 : index;
}

/** Form values → the API body (empty texts become null, the fiscal code normalized). */
export function personBody(values: ClientValues) {
  return {
    firstName: values.firstName,
    lastName: values.lastName,
    birthDate: values.birthDate,
    email: values.email,
    phone: values.phone || null,
    fiscalCode: normalizeFiscalCode(values.fiscalCode),
  };
}
