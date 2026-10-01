import { z } from "zod";

/** An SMTP account (provider `smtp`); mirrors `SmtpSettings` and `MessagingAccount`, the API stays the authority. */
export const smtpAccountSchema = z.object({
  name: z.string().trim().min(1, "validation.notEmpty").max(100, "validation.maximumLength"),
  host: z.string().trim().min(1, "validation.messaging.host").max(255, "validation.maximumLength"),
  port: z
    .string()
    .trim()
    .regex(/^\d{1,5}$/, "validation.messaging.port")
    .refine((value) => Number(value) >= 1 && Number(value) <= 65535, "validation.messaging.port"),
  security: z.enum(["None", "StartTls", "SslOnConnect"]),
  username: z.string().trim().max(255, "validation.maximumLength"),
  password: z.string().max(500, "validation.maximumLength"),
  fromAddress: z.email("validation.email"),
  fromName: z.string().trim().max(100, "validation.maximumLength"),
});

export type SmtpAccountValues = z.output<typeof smtpAccountSchema>;

export const testMessageSchema = z.object({
  recipient: z.email("validation.email"),
  language: z.enum(["it", "en"]),
});

/** A rule row of the editor; `role` "*" = any role. */
export type RuleRow = {
  key: string;
  purpose: string;
  role: string;
  accountId: string;
  priority: number;
};

/** Rows → request: any role becomes null, priorities are whole numbers ≥ 0. */
export function rulesRequest(rows: readonly RuleRow[]) {
  return rows.map((row) => ({
    purpose: row.purpose,
    role: row.role === "*" ? null : row.role,
    accountId: row.accountId,
    priority: Math.max(0, Math.trunc(row.priority) || 0),
  }));
}
