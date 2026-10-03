import { z } from "zod";

import { EMAIL_MAX, FISCAL_CODE, NAME_MAX, normalizeFiscalCode, PHONE } from "@/features/clients";

const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

/** `YYYY-MM-DD` of today in the browser's calendar. */
function today(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const name = (key: string) => z.string().trim().min(1, key).max(NAME_MAX, key);

/**
 * The personal data of an employee (Q55, legacy create form): first and last name, birth date and e-mail (also the
 * user name) are required; phone and fiscal code are optional, the fiscal code unique (checked by the API).
 */
export const employeeSchema = z.object({
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
      (value) => value.trim() === "" || FISCAL_CODE.test(normalizeFiscalCode(value)),
      "validation.person.fiscalCode",
    ),
});

export type EmployeeValues = z.output<typeof employeeSchema>;

/** A new employee: the personal data and whether it can sign in at once (legacy "active", D-06 invitation). */
export const newEmployeeSchema = employeeSchema.extend({ canSignIn: z.boolean() });

export type NewEmployeeValues = z.output<typeof newEmployeeSchema>;

/** Editing an employee: the user name (unique, Q52) changes too. */
export const editEmployeeSchema = employeeSchema.extend({
  userName: z
    .string()
    .trim()
    .min(1, "validation.user.userName")
    .max(EMAIL_MAX, "validation.user.userName")
    .refine((value) => !/\s/.test(value), "validation.user.userName"),
});

export type EditEmployeeValues = z.output<typeof editEmployeeSchema>;

/** Form values → the API body (empty texts become null, the fiscal code normalized). */
export function employeeBody(values: EmployeeValues) {
  const fiscalCode = normalizeFiscalCode(values.fiscalCode);
  return {
    firstName: values.firstName,
    lastName: values.lastName,
    birthDate: values.birthDate,
    email: values.email,
    phone: values.phone || null,
    fiscalCode: fiscalCode || null,
  };
}
