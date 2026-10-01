import { brandingTokens } from "@auxilia/ui/lib/branding";
import { describe, expect, it } from "vitest";

import { displayValue, groupByModule, parseInput } from "./components/settings/tenant-settings";
import {
  brandingSchema,
  brandingValues,
  changedSettings,
  sixDigitColor,
  type BrandingValues,
} from "./schemas/branding";
import type { TenantBranding, TenantSetting } from "./settings-api";

const setting = (key: string, module: string): TenantSetting => ({
  key,
  module,
  kind: "integer",
  choices: null,
  defaultValue: 7,
  platformValue: null,
  tenantValue: null,
  effectiveValue: 7,
  source: "Default",
  hasTenantValue: false,
});

const branding: TenantBranding = {
  appName: "Auxilia Digitale",
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

describe("settings editor", () => {
  it("groups settings by module in the order of the API", () => {
    const groups = groupByModule([
      setting("cases.a", "Cases"),
      setting("cases.b", "Cases"),
      setting("auth.c", "Identity"),
    ]);

    expect(groups.map((group) => [group.module, group.items.length])).toEqual([
      ["Cases", 2],
      ["Identity", 1],
    ]);
  });

  it("shows JSON values and parses inputs by kind", () => {
    expect(displayValue(7)).toBe("7");
    expect(displayValue(true)).toBe("true");
    expect(displayValue("Gradient")).toBe("Gradient");
    expect(displayValue(null)).toBe("");
    expect(parseInput("integer", " 30 ")).toBe(30);
    expect(parseInput("number", "1.5")).toBe(1.5);
    expect(parseInput("integer", "many")).toBeUndefined();
    expect(parseInput("integer", "")).toBeUndefined();
    expect(parseInput("string", " x ")).toBe(" x ");
  });
});

describe("branding form", () => {
  const values = brandingValues(branding);

  it("starts from the branding and validates names and colours", () => {
    expect(values).toMatchObject({ themeFill: "Gradient", backgroundKind: "Gradient" });
    expect(brandingSchema.safeParse(values).success).toBe(true);

    const invalid = brandingSchema.safeParse({ ...values, appName: " ", primaryColor: "red" });
    expect(invalid.error?.issues.map((issue) => [issue.path[0], issue.message])).toEqual([
      ["appName", "validation.branding.appName"],
      ["primaryColor", "validation.branding.color"],
    ]);
    expect(brandingSchema.safeParse({ ...values, accentColor: "#abc" }).success).toBe(true);
  });

  it("stores only the changed fields, by setting key", () => {
    const next: BrandingValues = {
      ...values,
      appName: "Studio Rossi",
      useAppName: false,
      backgroundKind: "Image",
    };

    expect(changedSettings(values, values)).toEqual({});
    expect(changedSettings(values, next)).toEqual({
      "branding.useAppName": false,
      "branding.appName": "Studio Rossi",
      "branding.background.kind": "Image",
    });
  });

  it("gives the native picker six-digit colours", () => {
    expect(sixDigitColor("#AbC")).toBe("#aabbcc");
    expect(sixDigitColor("#667EEA")).toBe("#667eea");
    expect(sixDigitColor("nope")).toBe("#000000");
  });

  it("previews the accessible shades the app will use", () => {
    const tokens = brandingTokens({ primaryColor: "#ffee00", accentColor: "#ffee00" }).light;
    expect(tokens["--primary-foreground"]).toBe("#0a0a0a");
  });
});
