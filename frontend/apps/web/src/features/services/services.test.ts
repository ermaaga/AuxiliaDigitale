import { describe, expect, it } from "vitest";

import { queryKey } from "@/lib/api/query-keys";

import { movedOrder, servicesKey, type ServiceFolder } from "./api";
import { NONE, serviceBody, serviceSchema } from "./schemas/service";

const valid = {
  name: " Dichiarazione ",
  description: "",
  price: "150,5",
  durationDays: "30",
  categoryId: NONE,
  specializationId: "spec-1",
  isActive: true,
};

describe("service schema (mirrors the API limits)", () => {
  it("accepts a price with a comma or a dot and at most two decimals", () => {
    const parsed = serviceSchema.parse(valid);
    expect(parsed.name).toBe("Dichiarazione");
    expect(parsed.price).toBe("150.5");
    expect(serviceSchema.safeParse({ ...valid, price: "0" }).success).toBe(true);
    expect(serviceSchema.safeParse({ ...valid, price: "1.234" }).error?.issues[0]?.message).toBe(
      "validation.services.price",
    );
    expect(serviceSchema.safeParse({ ...valid, price: "-1" }).error?.issues[0]?.message).toBe(
      "validation.services.price",
    );
  });

  it("requires a name and a duration between 1 and 3650 days", () => {
    const issues = serviceSchema
      .safeParse({ ...valid, name: " ", durationDays: "0", description: "x".repeat(2001) })
      .error?.issues.map((issue) => issue.message);
    expect(issues).toEqual([
      "validation.services.name",
      "validation.services.description",
      "validation.services.durationDays",
    ]);
    expect(serviceSchema.safeParse({ ...valid, durationDays: "3650" }).success).toBe(true);
    expect(serviceSchema.safeParse({ ...valid, durationDays: "3651" }).success).toBe(false);
  });

  it("sends numbers and null for empty choices", () => {
    expect(serviceBody(serviceSchema.parse(valid))).toEqual({
      name: "Dichiarazione",
      description: null,
      price: 150.5,
      durationDays: 30,
      categoryId: null,
      specializationId: "spec-1",
    });
  });
});

describe("folder order", () => {
  const folder = (id: string, parentId: string | null): ServiceFolder => ({
    id,
    parentId,
    name: id,
    sortOrder: 0,
    depth: parentId ? 1 : 0,
    path: id,
  });
  const tree = [folder("a", null), folder("a1", "a"), folder("a2", "a"), folder("b", null)];

  it("swaps a folder with its sibling and keeps the other siblings", () => {
    expect(movedOrder(tree, tree[3]!, -1)).toEqual(["b", "a"]);
    expect(movedOrder(tree, tree[1]!, 1)).toEqual(["a2", "a1"]);
  });

  it("does not move past the first or last sibling", () => {
    expect(movedOrder(tree, tree[0]!, -1)).toBeUndefined();
    expect(movedOrder(tree, tree[2]!, 1)).toBeUndefined();
  });
});

describe("query keys", () => {
  it("keeps every catalog query under the prefix the case pages share", () => {
    expect(servicesKey("demo")).toEqual(queryKey("demo", "cases", "services"));
    expect(servicesKey("demo", "folders", { serviceId: "s" })).toEqual(
      queryKey("demo", "cases", "services", { entity: "folders", serviceId: "s" }),
    );
  });
});
