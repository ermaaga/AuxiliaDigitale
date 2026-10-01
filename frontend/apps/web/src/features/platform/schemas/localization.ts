import { z } from "zod";

/** Mirrors `ResourceKey` (domain): the API stays the authority (unique key). */
export const resourceKeySchema = z.object({
  key: z
    .string()
    .trim()
    .max(200, "validation.localization.key")
    .regex(/^[A-Za-z][A-Za-z0-9_.-]*$/, "validation.localization.key"),
  category: z
    .string()
    .trim()
    .max(50, "validation.localization.category")
    .regex(/^[a-z][A-Za-z0-9]*$/, "validation.localization.category"),
  description: z.string().trim().max(500, "validation.maximumLength"),
});

export const keyDetailsSchema = resourceKeySchema.pick({ category: true, description: true });

export type ResourceKeyValues = z.output<typeof resourceKeySchema>;

/** Maximum length of a translation (`ResourceTranslation.ValueMaxLength`). */
export const TRANSLATION_MAX_LENGTH = 4000;

/** The first translations of a new key: only the languages with a value. */
export function initialTranslations(
  values: Readonly<Record<string, string>>,
): Record<string, string> | null {
  const filled = Object.entries(values)
    .map(([language, value]) => [language, value.trim()] as const)
    .filter(([, value]) => value !== "");
  return filled.length === 0 ? null : Object.fromEntries(filled);
}
