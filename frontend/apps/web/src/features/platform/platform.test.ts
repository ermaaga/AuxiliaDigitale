import { describe, expect, it } from "vitest";

import {
  PLATFORM_HOME,
  consoleTenantFromPath,
  platformHref,
  safePlatformNextPath,
  switchConsoleTenant,
  tenantConsoleHref,
} from "@/lib/href";

import { groupSecret } from "./components/activation-form";
import { statusLabel } from "./tenant-status";
import { filterTenants } from "./components/tenants-table";
import { activationSchema, activationTokenSchema } from "./schemas/activation";

const tenants = [
  {
    slug: "acme",
    displayName: "ACME Srl",
    status: "Active",
    schemaVersion: "v1",
    planCode: "standard",
  },
  {
    slug: "beta",
    displayName: "Beta Studio",
    status: "Suspended",
    schemaVersion: null,
    planCode: null,
  },
  {
    slug: "gamma",
    displayName: "Gamma",
    status: "Active",
    schemaVersion: "v1",
    planCode: "standard",
  },
];

describe("console links", () => {
  it("builds console and tenant paths", () => {
    expect(platformHref()).toBe("/platform");
    expect(platformHref("/login")).toBe("/platform/login");
    expect(platformHref("tenants")).toBe("/platform/tenants");
    expect(tenantConsoleHref("acme")).toBe("/platform/tenants/acme");
    expect(tenantConsoleHref("acme", "/jobs")).toBe("/platform/tenants/acme/jobs");
  });

  it("reads the selected tenant from the path and keeps the page when switching", () => {
    expect(consoleTenantFromPath("/platform/tenants")).toBeUndefined();
    expect(consoleTenantFromPath("/platform/tenants/acme")).toBe("acme");
    expect(consoleTenantFromPath("/platform/tenants/acme/jobs")).toBe("acme");
    expect(switchConsoleTenant("/platform/tenants/acme/jobs", "beta")).toBe(
      "/platform/tenants/beta/jobs",
    );
    expect(switchConsoleTenant("/platform/tenants/acme", "beta")).toBe("/platform/tenants/beta");
    expect(switchConsoleTenant("/platform/tenants", "beta")).toBe("/platform/tenants/beta");
  });

  it("returns to console pages only after sign-in (open redirect)", () => {
    expect(safePlatformNextPath("/platform/tenants/acme")).toBe("/platform/tenants/acme");
    expect(safePlatformNextPath(undefined)).toBe(PLATFORM_HOME);
    expect(safePlatformNextPath("/platform")).toBe(PLATFORM_HOME);
    expect(safePlatformNextPath("/platform/login")).toBe(PLATFORM_HOME);
    expect(safePlatformNextPath("/platform/activate?token=x")).toBe(PLATFORM_HOME);
    expect(safePlatformNextPath("/acme/dashboard")).toBe(PLATFORM_HOME);
    expect(safePlatformNextPath("https://evil.test/platform/x")).toBe(PLATFORM_HOME);
    expect(safePlatformNextPath("/platform//evil.test")).toBe(PLATFORM_HOME);
  });
});

describe("tenant list", () => {
  it("filters by name or slug and by status", () => {
    expect(filterTenants(tenants, undefined, undefined)).toHaveLength(3);
    expect(filterTenants(tenants, "STUDIO", undefined).map((t) => t.slug)).toEqual(["beta"]);
    expect(filterTenants(tenants, "gam", undefined).map((t) => t.slug)).toEqual(["gamma"]);
    expect(filterTenants(tenants, undefined, "Active").map((t) => t.slug)).toEqual([
      "acme",
      "gamma",
    ]);
    expect(filterTenants(tenants, "acme", "Suspended")).toEqual([]);
  });

  it("labels statuses with their translation, unknown ones as they are", () => {
    const t = Object.assign((key: string) => `«${key}»`, {
      has: (key: string) => key.endsWith(".Active"),
    });
    expect(statusLabel(t, "Active")).toBe("«app.platform.tenantStatus.Active»");
    expect(statusLabel(t, "Frozen", "acme")).toBe("acme · Frozen");
  });
});

describe("System account activation", () => {
  it("groups the setup key by four characters", () => {
    expect(groupSecret("JBSWY3DPEHPK3PXP")).toBe("JBSW Y3DP EHPK 3PXP");
    expect(groupSecret("ABCDEF")).toBe("ABCD EF");
  });

  it("validates token, password length, confirmation and code", () => {
    expect(activationTokenSchema.safeParse({ activationToken: "  " }).success).toBe(false);
    const valid = activationSchema.safeParse({
      password: "a-long-password",
      confirmPassword: "a-long-password",
      code: "123 456",
    });
    expect(valid.success && valid.data.code).toBe("123456");

    const invalid = activationSchema.safeParse({
      password: "short",
      confirmPassword: "different",
      code: "12345",
    });
    expect(invalid.success).toBe(false);
    const messages = Object.fromEntries(
      (invalid.error?.issues ?? []).map((issue) => [issue.path.join("."), issue.message]),
    );
    expect(messages).toMatchObject({
      password: "validation.password.tooShort",
      code: "validation.auth.totpInvalid",
    });
  });

  it("reports a password mismatch on the confirmation field", () => {
    const result = activationSchema.safeParse({
      password: "a-long-password",
      confirmPassword: "another-password",
      code: "123456",
    });
    expect(result.error?.issues.map((issue) => [issue.path.join("."), issue.message])).toEqual([
      ["confirmPassword", "app.auth.passwordMismatch"],
    ]);
  });
});
