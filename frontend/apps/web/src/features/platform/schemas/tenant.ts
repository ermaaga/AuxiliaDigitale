import { z } from "zod";

/** Slug rule of the API (`TenantSlug`): 3–40 lower-case letters, digits and inner hyphens. */
export const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])$/;

const required = (max: number) =>
  z.string().trim().min(1, "validation.notEmpty").max(max, "validation.maximumLength");

/** Mirrors the API limits (`Tenant`, `Person`); the API stays the authority (reserved slugs, uniqueness, time zones). */
export const createTenantSchema = z
  .object({
    slug: z.string().trim().regex(SLUG_PATTERN, "validation.tenant.slug"),
    displayName: required(200),
    defaultLanguage: z.enum(["it", "en"]),
    timeZone: z.string().min(1, "validation.tenant.timeZone"),
    inviteAdministrator: z.boolean(),
    adminEmail: z.string().trim(),
    adminFirstName: z.string().trim(),
    adminLastName: z.string().trim(),
  })
  .superRefine((value, context) => {
    if (!value.inviteAdministrator) {
      return;
    }

    if (!z.email().safeParse(value.adminEmail).success) {
      context.addIssue({ code: "custom", path: ["adminEmail"], message: "validation.email" });
    }

    for (const field of ["adminFirstName", "adminLastName"] as const) {
      if (value[field].length === 0) {
        context.addIssue({ code: "custom", path: [field], message: "validation.notEmpty" });
      }
    }
  });

export type CreateTenantValues = z.input<typeof createTenantSchema>;

export const updateTenantSchema = z.object({
  displayName: required(200),
  timeZone: z.string().min(1, "validation.tenant.timeZone"),
});

export const administratorSchema = z.object({
  email: z.email("validation.email"),
  firstName: required(100),
  lastName: required(100),
});

/** API field names → form fields (`administrator.email` → `adminEmail`). */
export const CREATE_TENANT_FIELDS: Record<string, keyof CreateTenantValues> = {
  slug: "slug",
  displayName: "displayName",
  defaultLanguage: "defaultLanguage",
  timeZone: "timeZone",
  "administrator.email": "adminEmail",
  "administrator.firstName": "adminFirstName",
  "administrator.lastName": "adminLastName",
};
