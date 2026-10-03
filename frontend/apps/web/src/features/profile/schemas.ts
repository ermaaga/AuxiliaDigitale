import { z } from "zod";

import { EMAIL_MAX, NAME_MAX, PHONE } from "@/features/clients";

const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

const name = (key: string) => z.string().trim().min(1, key).max(NAME_MAX, key);

/** The own personal data (B-03 `PUT /me/profile`): the person rules; the e-mail is required, the user name read-only. */
export const profileSchema = z.object({
  firstName: name("validation.person.firstName"),
  lastName: name("validation.person.lastName"),
  email: z
    .string()
    .trim()
    .max(EMAIL_MAX, "validation.person.email")
    .refine((value) => EMAIL.test(value), "validation.person.email"),
  phone: z
    .string()
    .trim()
    .refine((value) => value === "" || PHONE.test(value), "validation.person.phone"),
});

export type ProfileValues = z.output<typeof profileSchema>;

/** Own password change (F04, F35): the current password, the new one twice; the policy is checked by the API. */
export const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, "validation.notEmpty"),
    newPassword: z.string().min(1, "validation.notEmpty"),
    confirmPassword: z.string(),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    path: ["confirmPassword"],
    message: "app.auth.passwordMismatch",
  });

export type PasswordValues = z.output<typeof passwordSchema>;
