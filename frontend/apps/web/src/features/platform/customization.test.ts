import { describe, expect, it } from "vitest";

import { moveColumn } from "./customization-api";
import { customFieldBody, customFieldSchema, optionsOf } from "./schemas/custom-field";

const values = {
  key: "CAF",
  label: "CAF",
  type: "Boolean",
  options: "",
  isRequired: false,
  groupName: "Area",
  badgeColor: "#72fa29",
  visibleOnGrid: true,
  dashboardCounter: true,
  order: "1",
} as const;

describe("custom field form", () => {
  it("accepts the legacy CAF field", () => {
    expect(customFieldSchema.safeParse(values).success).toBe(true);
  });

  it("refuses bad keys, missing choices, colours without group and counters on non-booleans", () => {
    const issues = (input: Record<string, unknown>) =>
      customFieldSchema
        .safeParse({ ...values, ...input })
        .error?.issues.map((issue) => [issue.path[0], issue.message]);

    expect(issues({ key: "1abc" })).toEqual([["key", "validation.customFields.key"]]);
    expect(issues({ type: "Select", dashboardCounter: false })).toEqual([
      ["options", "validation.customFields.options"],
    ]);
    expect(issues({ type: "Select", options: "a\na", dashboardCounter: false })).toEqual([
      ["options", "validation.customFields.options"],
    ]);
    expect(issues({ groupName: "" })).toEqual([
      ["badgeColor", "validation.customFields.badgeColor"],
    ]);
    expect(issues({ type: "Text" })).toEqual([
      ["dashboardCounter", "validation.customFields.dashboardCounter"],
    ]);
    expect(issues({ order: "x" })).toEqual([["order", "validation.customFields.order"]]);
  });

  it("builds the request: options only for selects, colour only with a group", () => {
    expect(optionsOf(" a \n\n b\n")).toEqual(["a", "b"]);
    expect(
      customFieldBody({ ...values, type: "Select", options: "x\ny", dashboardCounter: true }),
    ).toMatchObject({
      options: ["x", "y"],
      dashboardCounter: false,
    });
    expect(customFieldBody({ ...values, groupName: "", badgeColor: "#72fa29" })).toMatchObject({
      groupName: null,
      badgeColor: null,
      order: 1,
    });
  });
});

describe("grid layout editor", () => {
  it("moves columns within the list", () => {
    expect(moveColumn(["a", "b", "c"], 1, -1)).toEqual(["b", "a", "c"]);
    expect(moveColumn(["a", "b", "c"], 1, 1)).toEqual(["a", "c", "b"]);
    expect(moveColumn(["a", "b"], 0, -1)).toEqual(["a", "b"]);
    expect(moveColumn(["a", "b"], 1, 1)).toEqual(["a", "b"]);
  });
});
