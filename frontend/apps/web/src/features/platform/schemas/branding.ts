import { z } from "zod";

import type { TenantBranding } from "../settings-api";

/** `#rgb` or `#rrggbb`, as the API accepts (`SettingRules.IsHexColor`). */
export const HEX_COLOR = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;

const color = z.string().trim().regex(HEX_COLOR, "validation.branding.color");

/** The branding form; mirrors the setting definitions (`BrandingSettings`), the API stays the authority. */
export const brandingSchema = z.object({
  useAppName: z.boolean(),
  appName: z
    .string()
    .trim()
    .min(1, "validation.branding.appName")
    .max(100, "validation.branding.appName"),
  themeFill: z.enum(["Gradient", "Solid"]),
  primaryColor: color,
  accentColor: color,
  backgroundKind: z.enum(["Gradient", "Solid", "Image"]),
  backgroundStartColor: color,
  backgroundEndColor: color,
  backgroundColor: color,
});

export type BrandingValues = z.output<typeof brandingSchema>;

/** Form field → setting key. */
export const BRANDING_SETTING_KEYS: Readonly<Record<keyof BrandingValues, string>> = {
  useAppName: "branding.useAppName",
  appName: "branding.appName",
  themeFill: "branding.theme.fill",
  primaryColor: "branding.theme.primaryColor",
  accentColor: "branding.theme.accentColor",
  backgroundKind: "branding.background.kind",
  backgroundStartColor: "branding.background.startColor",
  backgroundEndColor: "branding.background.endColor",
  backgroundColor: "branding.background.color",
};

export function brandingValues(branding: TenantBranding): BrandingValues {
  return {
    useAppName: branding.useAppName,
    appName: branding.appName,
    themeFill: branding.themeFill === "Solid" ? "Solid" : "Gradient",
    primaryColor: branding.primaryColor,
    accentColor: branding.accentColor,
    backgroundKind:
      branding.background.kind === "Solid" || branding.background.kind === "Image"
        ? branding.background.kind
        : "Gradient",
    backgroundStartColor: branding.background.startColor,
    backgroundEndColor: branding.background.endColor,
    backgroundColor: branding.background.color,
  };
}

/** Only what changed, keyed by setting: an unchanged value is not written at the tenant level. */
export function changedSettings(
  before: BrandingValues,
  after: BrandingValues,
): Record<string, string | boolean> {
  const changes: Record<string, string | boolean> = {};
  for (const field of Object.keys(BRANDING_SETTING_KEYS) as Array<keyof BrandingValues>) {
    if (before[field] !== after[field]) {
      changes[BRANDING_SETTING_KEYS[field]] = after[field];
    }
  }

  return changes;
}

/** `#abc` → `#aabbcc` for the native colour picker, which takes six digits only. */
export function sixDigitColor(value: string): string {
  const digits = /^#([0-9a-f]{3})$/i.exec(value)?.[1];
  if (digits) {
    return `#${[...digits].map((digit) => digit + digit).join("")}`.toLowerCase();
  }

  return HEX_COLOR.test(value) ? value.toLowerCase() : "#000000";
}
