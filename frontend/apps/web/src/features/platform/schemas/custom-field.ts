import { z } from "zod";

import { HEX_COLOR } from "./branding";

/** Mirrors `CustomFieldDefinition` (domain): the API stays the authority (key unique per entity, entity known). */
export const customFieldSchema = z
  .object({
    key: z
      .string()
      .trim()
      .regex(/^[A-Za-z][A-Za-z0-9_]{0,49}$/, "validation.customFields.key"),
    label: z
      .string()
      .trim()
      .min(1, "validation.customFields.label")
      .max(100, "validation.customFields.label"),
    type: z.enum(["Text", "Number", "Date", "Boolean", "Select", "MultiSelect"]),
    options: z.string(),
    isRequired: z.boolean(),
    groupName: z.string().trim().max(50, "validation.customFields.groupName"),
    badgeColor: z.string().trim(),
    visibleOnGrid: z.boolean(),
    dashboardCounter: z.boolean(),
    order: z
      .string()
      .trim()
      .regex(/^\d{1,4}$/, "validation.customFields.order"),
  })
  .superRefine((value, context) => {
    const options = optionsOf(value.options);
    const withOptions = value.type === "Select" || value.type === "MultiSelect";
    if (
      withOptions &&
      (options.length === 0 ||
        new Set(options).size !== options.length ||
        options.some((option) => option.length > 100))
    ) {
      context.addIssue({
        code: "custom",
        path: ["options"],
        message: "validation.customFields.options",
      });
    }

    if (value.badgeColor !== "" && (value.groupName === "" || !HEX_COLOR.test(value.badgeColor))) {
      context.addIssue({
        code: "custom",
        path: ["badgeColor"],
        message: "validation.customFields.badgeColor",
      });
    }

    if (value.dashboardCounter && value.type !== "Boolean") {
      context.addIssue({
        code: "custom",
        path: ["dashboardCounter"],
        message: "validation.customFields.dashboardCounter",
      });
    }
  });

export type CustomFieldValues = z.output<typeof customFieldSchema>;

/** One option per line, trimmed, empty lines dropped. */
export function optionsOf(text: string): string[] {
  return text
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line !== "");
}

/** The form values → request body (options only for the selects, colour only with a group, counter only for booleans). */
export function customFieldBody(values: CustomFieldValues) {
  const withOptions = values.type === "Select" || values.type === "MultiSelect";
  return {
    label: values.label,
    options: withOptions ? optionsOf(values.options) : [],
    isRequired: values.isRequired,
    groupName: values.groupName || null,
    badgeColor: values.groupName && values.badgeColor ? values.badgeColor : null,
    visibleOnGrid: values.visibleOnGrid,
    dashboardCounter: values.type === "Boolean" && values.dashboardCounter,
    order: Number(values.order),
  };
}
