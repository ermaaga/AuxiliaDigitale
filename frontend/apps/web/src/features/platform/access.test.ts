import { describe, expect, it } from "vitest";

import {
  accessKey,
  byModule,
  differsFromDefaults,
  toggledPermissions,
  type RolePermission,
} from "./access-api";
import { memberName } from "./components/access/specialization-members";
import { specializationBody, specializationSchema } from "./schemas/specialization";
import { tenantKey } from "./tenant-api";

const permissions: RolePermission[] = [
  {
    code: "cases.cases.view",
    module: "cases",
    roles: ["Administrator", "Employee", "Client"],
    defaultRoles: ["Administrator", "Employee", "Client"],
  },
  {
    code: "cases.cases.manage",
    module: "cases",
    roles: ["Administrator"],
    defaultRoles: ["Administrator", "Employee"],
  },
  {
    code: "identity.sessions.view",
    module: "identity",
    roles: ["Administrator"],
    defaultRoles: ["Administrator"],
  },
];

describe("role permissions", () => {
  it("computes the whole set of a role after a toggle", () => {
    expect(toggledPermissions(permissions, "Employee", "cases.cases.manage", true)).toEqual([
      "cases.cases.view",
      "cases.cases.manage",
    ]);
    expect(toggledPermissions(permissions, "Administrator", "cases.cases.view", false)).toEqual([
      "cases.cases.manage",
      "identity.sessions.view",
    ]);
  });

  it("tells customized roles and groups by module in the API order", () => {
    expect(differsFromDefaults(permissions, "Employee")).toBe(true);
    expect(differsFromDefaults(permissions, "Administrator")).toBe(false);
    expect(differsFromDefaults(permissions, "Client")).toBe(false);
    expect(byModule(permissions).map(([module, items]) => [module, items.length])).toEqual([
      ["cases", 2],
      ["identity", 1],
    ]);
  });

  it("nests its queries under the invalidated prefix", () => {
    const prefix = tenantKey("demo", "access");
    expect(accessKey("demo", "members", { id: "x" }).slice(0, prefix.length)).toEqual([...prefix]);
  });
});

describe("specializations", () => {
  const valid = {
    name: " Fisioterapia ",
    role: "Employee" as const,
    description: "",
    email: "fisio@example.com",
    workPhone: "+39 06 123-456",
    isPrivate: true,
  };

  it("validates as the API does and sends empty texts as null", () => {
    const parsed = specializationSchema.parse(valid);
    expect(specializationBody(parsed)).toEqual({
      name: "Fisioterapia",
      description: null,
      email: "fisio@example.com",
      workPhone: "+39 06 123-456",
      isPrivate: true,
    });

    const invalid = specializationSchema.safeParse({
      ...valid,
      name: " ",
      email: "nope",
      workPhone: "call me",
      description: "x".repeat(1001),
    });
    expect(invalid.error?.issues.map((issue) => [issue.path[0], issue.message])).toEqual([
      ["name", "validation.specializations.name"],
      ["description", "validation.specializations.description"],
      ["email", "validation.specializations.email"],
      ["workPhone", "validation.specializations.workPhone"],
    ]);
    expect(specializationSchema.safeParse({ ...valid, role: "Administrator" }).success).toBe(false);
  });

  it("names a member by full name and user name", () => {
    const member = {
      userId: "1",
      userName: "anna",
      fullName: "Anna Rossi",
      email: null,
      isActive: true,
    };
    expect(memberName(member)).toBe("Anna Rossi (anna)");
    expect(memberName({ ...member, fullName: null })).toBe("anna");
  });
});
