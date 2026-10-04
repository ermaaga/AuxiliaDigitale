import { z } from "zod";

/** Server limits (`TaskItem` in the API, B-26). */
export const TITLE_MAX = 200;
export const NOTES_MAX = 2000;
export const ACTIVITY_MAX = 2000;

export const taskSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, "validation.tasks.title")
    .max(TITLE_MAX, "validation.tasks.title"),
  notes: z.string().max(NOTES_MAX, "validation.tasks.notes"),
  dueOn: z.string(),
  assigneeUserId: z.string().min(1, "validation.tasks.assignee"),
  clientId: z.string().optional(),
  caseId: z.string().optional(),
});

export type TaskValues = z.input<typeof taskSchema>;

export const activitySchema = z.object({
  kind: z.enum(["Note", "Call", "Meeting", "Email"], { message: "validation.activities.kind" }),
  text: z
    .string()
    .trim()
    .min(1, "validation.activities.text")
    .max(ACTIVITY_MAX, "validation.activities.text"),
});

export type ActivityValues = z.input<typeof activitySchema>;
