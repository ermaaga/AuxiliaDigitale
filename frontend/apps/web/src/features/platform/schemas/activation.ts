import { z } from "zod";

/** Minimum password length of System users (API `PlatformIdentityPolicy.PasswordMinLength`, the authority). */
export const PLATFORM_PASSWORD_MIN_LENGTH = 12;

/** Authenticator code: six digits, spaces allowed while typing (`123 456`). */
export const totpCode = z
  .string()
  .transform((value) => value.replace(/\s/g, ""))
  .pipe(z.string().regex(/^\d{6}$/, "validation.auth.totpInvalid"));

/** Step 1: the one-use activation code printed by `auxctl platform users add|reset`. */
export const activationTokenSchema = z.object({
  activationToken: z.string().trim().min(1, "validation.notEmpty"),
});

/** Step 2: the password (twice) and the first code of the authenticator app. */
export const activationSchema = z
  .object({
    password: z.string().min(PLATFORM_PASSWORD_MIN_LENGTH, "validation.password.tooShort"),
    confirmPassword: z.string(),
    code: totpCode,
  })
  .refine((value) => value.password === value.confirmPassword, {
    path: ["confirmPassword"],
    message: "app.auth.passwordMismatch",
  });
