import { ApiError } from "@auxilia/api-client";
import { describe, expect, it, vi } from "vitest";

import { applyApiErrors } from "@/components/forms/form";

import {
  PLATFORM_HOME,
  consoleTenantFromPath,
  platformHref,
  safePlatformNextPath,
  switchConsoleTenant,
  tenantConsoleHref,
} from "@/lib/href";

import { groupSecret } from "@/components/totp-enrollment";
import { statusLabel } from "./tenant-status";
import { filterTenants } from "./components/tenants-table";
import { activationSchema, activationTokenSchema } from "./schemas/activation";
import { CREATE_TENANT_FIELDS, createTenantSchema } from "./schemas/tenant";
import { rolesText } from "./labels";

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
    const archived = { ...tenants[2]!, slug: "old", status: "Archived" };
    expect(
      filterTenants([...tenants, archived], undefined, undefined).map((t) => t.slug),
    ).not.toContain("old");
    expect(filterTenants([...tenants, archived], undefined, "Archived").map((t) => t.slug)).toEqual(
      ["old"],
    );
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

describe("tenant creation", () => {
  const values = {
    slug: "studio-rossi",
    displayName: "Studio Rossi",
    defaultLanguage: "it" as const,
    timeZone: "Europe/Rome",
    inviteAdministrator: false,
    adminEmail: "",
    adminFirstName: "",
    adminLastName: "",
  };
  const issues = (input: Record<string, unknown>) =>
    Object.fromEntries(
      (createTenantSchema.safeParse({ ...values, ...input }).error?.issues ?? []).map((issue) => [
        issue.path.join("."),
        issue.message,
      ]),
    );

  it("follows the slug rule of the API", () => {
    expect(issues({})).toEqual({});
    expect(issues({ slug: "Studio" })).toEqual({ slug: "validation.tenant.slug" });
    expect(issues({ slug: "a" })).toEqual({ slug: "validation.tenant.slug" });
    expect(issues({ slug: "-studio" })).toEqual({ slug: "validation.tenant.slug" });
  });

  it("asks for the Administrator only when the invitation is chosen", () => {
    expect(issues({ inviteAdministrator: true })).toEqual({
      adminEmail: "validation.email",
      adminFirstName: "validation.notEmpty",
      adminLastName: "validation.notEmpty",
    });
    expect(
      issues({
        inviteAdministrator: true,
        adminEmail: "anna@rossi.test",
        adminFirstName: "Anna",
        adminLastName: "Rossi",
      }),
    ).toEqual({});
  });

  it("puts the API errors of the Administrator on the form fields", () => {
    const setError = vi.fn();
    const error = new ApiError(400, {
      errorCode: "AUX-10020",
      errors: {
        "administrator.email": ["validation.email"],
        slug: ["validation.tenant.slugReserved"],
      },
    });

    expect(applyApiErrors(error, setError, [], CREATE_TENANT_FIELDS)).toBe(true);
    expect(setError).toHaveBeenCalledWith("adminEmail", {
      type: "server",
      message: "validation.email",
    });
    expect(setError).toHaveBeenCalledWith("slug", {
      type: "server",
      message: "validation.tenant.slugReserved",
    });
  });

  it("names the roles of a module", () => {
    const t = Object.assign((key: string) => `«${key}»`, { has: () => true });
    expect(rolesText(t, [])).toBe("«app.platform.modules.none»");
    expect(rolesText(t, ["Administrator", "Client"])).toBe("«Administrator», «Client»");
  });
});
