import { describe, expect, it } from "vitest";

import { initials } from "@/components/shell/app-shell";
import { safeNextPath, tenantHref } from "./href";

describe("tenantHref", () => {
  it("prefixes the tenant", () => {
    expect(tenantHref("acme")).toBe("/acme");
    expect(tenantHref("acme", "/")).toBe("/acme");
    expect(tenantHref("acme", "/cases")).toBe("/acme/cases");
    expect(tenantHref("acme", "login")).toBe("/acme/login");
  });
});

describe("safeNextPath", () => {
  it("keeps pages of the same tenant and refuses everything else (open redirect)", () => {
    expect(safeNextPath("acme", "/acme/cases?page=2")).toBe("/acme/cases?page=2");
    expect(safeNextPath("acme", "/acme")).toBe("/acme");
    expect(safeNextPath("acme", undefined)).toBe("/acme");
    expect(safeNextPath("acme", "/other/cases")).toBe("/acme");
    expect(safeNextPath("acme", "/acmeevil/x")).toBe("/acme");
    expect(safeNextPath("acme", "https://evil.test/acme/x")).toBe("/acme");
    expect(safeNextPath("acme", "//evil.test")).toBe("/acme");
    expect(safeNextPath("acme", "/acme//evil.test")).toBe("/acme");
    expect(safeNextPath("acme", "/acme/\\evil.test")).toBe("/acme");
  });
});

describe("initials", () => {
  it("takes up to two initials of the user name", () => {
    expect(initials("mario.rossi")).toBe("MR");
    expect(initials("Anna Maria Bianchi")).toBe("AM");
    expect(initials("admin")).toBe("AD");
    expect(initials("")).toBe("?");
  });
});
