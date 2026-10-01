import type { CSSProperties } from "react";
import type { components } from "@auxilia/api-client";
import {
  parseHexColor,
  readableForeground,
  relativeLuminance,
  toHex,
} from "@auxilia/ui/lib/branding";

import { DEFAULT_APP_NAME } from "@/lib/app";

/** Public branding of a tenant (`GET /branding`, F23). */
export type Branding = components["schemas"]["BrandingResponse"];

export type BrandingImage = "logo" | "background";

/** The platform branding: what a tenant shows until the System changes it, and when the API cannot be reached. */
export const DEFAULT_BRANDING: Branding = {
  appName: DEFAULT_APP_NAME,
  useAppName: true,
  themeFill: "Gradient",
  primaryColor: "#667eea",
  accentColor: "#764ba2",
  background: {
    kind: "Gradient",
    startColor: "#667eea",
    endColor: "#764ba2",
    color: "#667eea",
    imageVersion: null,
  },
  logoVersion: null,
};

/** Same-origin URL of a branding image (served by the web app, which adds the tenant header the API needs). */
export function brandingImageUrl(tenant: string, image: BrandingImage, version: string): string {
  return `/api/branding/${encodeURIComponent(tenant)}/${image}?v=${encodeURIComponent(version)}`;
}

/** What the shells render: the name (always, as text or as the logo's alternative text) and the logo when shown. */
export type BrandIdentity = { appName: string; logoUrl?: string };

export function brandIdentity(tenant: string, branding: Branding): BrandIdentity {
  const logoUrl =
    !branding.useAppName && branding.logoVersion
      ? brandingImageUrl(tenant, "logo", branding.logoVersion)
      : undefined;
  return { appName: branding.appName, logoUrl };
}

/**
 * Style of the login background panel. Colours are parsed and written again (never copied from input); with an image a
 * dark veil keeps the white app name readable; on colours the text is white or ink, whichever reads better. `undefined`
 * when nothing valid is configured: the panel keeps the brand gradient token.
 */
export function loginBackground(
  tenant: string,
  background: Branding["background"],
): CSSProperties | undefined {
  if (background.kind === "Image" && background.imageVersion) {
    const url = brandingImageUrl(tenant, "background", background.imageVersion);
    return {
      backgroundImage: `linear-gradient(rgb(0 0 0 / 0.45), rgb(0 0 0 / 0.45)), url("${url}")`,
      backgroundSize: "cover",
      backgroundPosition: "center",
      color: "#ffffff",
    };
  }

  if (background.kind === "Solid") {
    const color = parseHexColor(background.color);
    return color
      ? { backgroundColor: toHex(color), color: toHex(readableForeground(color)) }
      : undefined;
  }

  const start = parseHexColor(background.startColor);
  const end = parseHexColor(background.endColor);
  if (!start || !end) {
    return undefined;
  }

  // The text sits over both ends: it follows the darker one.
  const darker = relativeLuminance(start) <= relativeLuminance(end) ? start : end;
  return {
    backgroundImage: `linear-gradient(135deg, ${toHex(start)}, ${toHex(end)})`,
    color: toHex(readableForeground(darker)),
  };
}
