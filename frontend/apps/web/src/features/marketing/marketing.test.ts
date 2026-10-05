import { describe, expect, it } from "vitest";

import { campaignProgress, campaignVariant } from "./components/campaigns";
import { customValue, emptyCondition, emptyRule, fromRule, toRule } from "./rule";

describe("segment rules", () => {
  it("are incomplete until every condition has a value", () => {
    expect(toRule(emptyRule())).toBeUndefined();
    expect(
      toRule({
        match: "any",
        conditions: [{ field: "employee", op: "none", value: "", key: "" }],
        groups: [],
      }),
    ).toEqual({
      match: "any",
      conditions: [{ field: "employee", op: "none", value: null, key: null }],
      groups: [],
    });
    expect(
      toRule({
        match: "all",
        conditions: [emptyCondition()],
        groups: [{ match: "any", conditions: [] }],
      }),
    ).toBeUndefined();
  });

  it("send ages as numbers and custom field values as JSON literals, and come back the same", () => {
    const rule = toRule({
      match: "all",
      conditions: [
        { field: "age", op: "atLeast", value: "18", key: "" },
        { field: "customField", op: "is", value: "true", key: "caf" },
      ],
      groups: [
        { match: "any", conditions: [{ field: "status", op: "is", value: "Active", key: "" }] },
      ],
    })!;
    expect(rule.conditions.map((condition) => condition.value)).toEqual([18, true]);
    expect(rule.conditions[1]!.key).toBe("caf");
    expect(fromRule(rule).conditions.map((condition) => condition.value)).toEqual(["18", "true"]);
    expect(customValue(" 12 ")).toBe(12);
    expect(customValue("CAF")).toBe("CAF");
  });
});

describe("campaigns", () => {
  it("show progress and status colours", () => {
    expect(
      campaignProgress({
        status: "Sending",
        recipientCount: 4,
        sentCount: 1,
        failedCount: 0,
        excludedCount: 1,
      }),
    ).toBe(50);
    expect(
      campaignProgress({
        status: "Sent",
        recipientCount: 0,
        sentCount: 0,
        failedCount: 0,
        excludedCount: 0,
      }),
    ).toBe(100);
    expect(campaignVariant("Failed")).toBe("destructive");
    expect(campaignVariant("Draft")).toBe("outline");
  });
});
