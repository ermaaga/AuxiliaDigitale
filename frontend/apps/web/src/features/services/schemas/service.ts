import { z } from "zod";

/** Server limits (`Service`, `ServiceCategory` in the API, F08). */
export const NAME_MAX = 200;
export const DESCRIPTION_MAX = 2000;
export const MAX_DURATION_DAYS = 3650;
export const CATEGORY_NAME_MAX = 100;

/** A service (legacy form): name, description, price in euro with two decimals, duration, category, specialization. */
export const serviceSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "validation.services.name")
    .max(NAME_MAX, "validation.services.name"),
  description: z.string().max(DESCRIPTION_MAX, "validation.services.description"),
  price: z
    .string()
    .trim()
    .transform((value) => value.replace(",", "."))
    .refine((value) => /^\d+(\.\d{1,2})?$/.test(value), "validation.services.price"),
  durationDays: z
    .string()
    .trim()
    .refine(
      (value) => /^\d+$/.test(value) && Number(value) >= 1 && Number(value) <= MAX_DURATION_DAYS,
      "validation.services.durationDays",
    ),
  categoryId: z.string(),
  specializationId: z.string(),
  isActive: z.boolean(),
});

export type ServiceValues = z.input<typeof serviceSchema>;
export type ServiceOutput = z.output<typeof serviceSchema>;

export const NONE = "none";

/** The body of a create or update (empty choices are null). */
export function serviceBody(values: ServiceOutput) {
  return {
    name: values.name,
    description: values.description.trim() || null,
    price: Number(values.price),
    durationDays: Number(values.durationDays),
    categoryId: values.categoryId === NONE ? null : values.categoryId,
    specializationId: values.specializationId === NONE ? null : values.specializationId,
  };
}
