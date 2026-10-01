import { z } from "zod";

/** Mirrors `Specialization` (domain): the API stays the authority (name unique per role). */
export const specializationSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "validation.specializations.name")
    .max(100, "validation.specializations.name"),
  role: z.enum(["Employee", "Client"]),
  description: z.string().trim().max(1000, "validation.specializations.description"),
  email: z
    .string()
    .trim()
    .max(256, "validation.specializations.email")
    .refine(
      (value) => value === "" || /^[^@\s]+@[^@\s]+$/.test(value),
      "validation.specializations.email",
    ),
  workPhone: z
    .string()
    .trim()
    .max(50, "validation.specializations.workPhone")
    .refine(
      (value) => value === "" || /^[0-9+()./ -]+$/.test(value),
      "validation.specializations.workPhone",
    ),
  isPrivate: z.boolean(),
});

export type SpecializationValues = z.output<typeof specializationSchema>;

/** The form values → request body (empty texts become null). */
export function specializationBody(values: SpecializationValues) {
  return {
    name: values.name,
    description: values.description || null,
    email: values.email || null,
    workPhone: values.workPhone || null,
    isPrivate: values.isPrivate,
  };
}
