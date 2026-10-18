import { z } from "zod";

/** Limits of the API (`ConnectedAccount`, `ConnectedAccountManager`). */
export const ADDRESS_MAX = 320;
export const USER_NAME_MAX = 320;
export const HOST_MAX = 253;
export const PASSWORD_MAX = 1000;

export const IMAP_SECURITY = ["SslOnConnect", "StartTls"] as const;
export type ImapSecurity = (typeof IMAP_SECURITY)[number];

const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const HOST =
  /^(?=.*[a-zA-Z])[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+$/;

/**
 * "Connect PEC / IMAP" (N05): the rules of the API before the connection test (server name only, never an IP
 * address; the allowed ports and the address the name resolves to are checked by the API).
 */
export const imapSchema = z.object({
  address: z
    .string()
    .trim()
    .max(ADDRESS_MAX, "validation.mailbox.address")
    .refine((value) => EMAIL.test(value), "validation.mailbox.address"),
  userName: z
    .string()
    .trim()
    .min(1, "validation.mailbox.userName")
    .max(USER_NAME_MAX, "validation.mailbox.userName"),
  password: z
    .string()
    .min(1, "validation.mailbox.password")
    .max(PASSWORD_MAX, "validation.mailbox.password"),
  host: z
    .string()
    .trim()
    .max(HOST_MAX, "validation.mailbox.host")
    .refine((value) => HOST.test(value), "validation.mailbox.host"),
  port: z.coerce
    .number()
    .int("validation.mailbox.port")
    .min(1, "validation.mailbox.port")
    .max(65535, "validation.mailbox.port"),
  security: z.enum(IMAP_SECURITY, "validation.mailbox.port"),
});

export type ImapValues = z.output<typeof imapSchema>;
export type ImapInput = z.input<typeof imapSchema>;

/** The connection type usually behind a port: 993 is TLS from the start, anything else STARTTLS. */
export function securityForPort(port: number | string | null | undefined): ImapSecurity {
  return Number(port) === 993 ? "SslOnConnect" : "StartTls";
}

/** Mail and calendar asked to the provider: at least one (incremental consent, N05). */
export const capabilitiesSchema = z
  .object({ mail: z.boolean(), calendar: z.boolean() })
  .refine((value) => value.mail || value.calendar, {
    path: ["mail"],
    message: "validation.mailbox.capabilities",
  });
