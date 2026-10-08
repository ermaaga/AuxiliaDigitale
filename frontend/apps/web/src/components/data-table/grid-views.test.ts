import { describe, expect, it } from "vitest";

import { currentView, gridViewsKey } from "./use-grid-views";

describe("personal grid views (F21)", () => {
  it("saves the visible state: hidden columns, the filters that are set, sort and default", () => {
    expect(
      currentView(
        " Mine ",
        ["email"],
        { status: "Active", name: undefined, type: "" },
        "-name",
        true,
      ),
    ).toEqual({
      name: "Mine",
      hiddenColumns: ["email"],
      filters: { status: "Active" },
      sort: "-name",
      isDefault: true,
    });
  });

  it("keys the views per tenant and grid", () => {
    expect(gridViewsKey("acme", "cases.cases")).not.toEqual(gridViewsKey("beta", "cases.cases"));
    expect(gridViewsKey("acme", "cases.cases")).not.toEqual(gridViewsKey("acme", "cases.services"));
  });
});
