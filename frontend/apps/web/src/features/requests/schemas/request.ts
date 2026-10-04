import { z } from "zod";

/** Server limits (`Request` in the API, F15). */
export const SUBJECT_MAX = 200;
export const MESSAGE_MAX = 4000;

const message = z
  .string()
  .trim()
  .min(1, "validation.requests.message")
  .max(MESSAGE_MAX, "validation.requests.message");

/** A new request (legacy form): type, subject, message; clients choose whether to ask their operator. */
export const requestSchema = z.object({
  type: z.enum(["Information", "General", "Support", "Appointment"], {
    message: "validation.requests.type",
  }),
  subject: z
    .string()
    .trim()
    .min(1, "validation.requests.subject")
    .max(SUBJECT_MAX, "validation.requests.subject"),
  message,
  askMyOperator: z.boolean(),
});

export const replySchema = z.object({ message });

export type RequestValues = z.input<typeof requestSchema>;
export type RequestOutput = z.output<typeof requestSchema>;
