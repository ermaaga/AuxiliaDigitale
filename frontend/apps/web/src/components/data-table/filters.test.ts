import { describe, expect, it } from "vitest";

import { dayBoundary } from "./filters";

describe("dayBoundary", () => {
  it("gives the start of the day and the start of the next one in the browser's time zone", () => {
    const from = dayBoundary("2026-10-04", "from")!;
    const to = dayBoundary("2026-10-04", "to")!;
    expect(new Date(from).getDate()).toBe(4);
    expect(new Date(from).getHours()).toBe(0);
    expect(new Date(to).getTime() - new Date(from).getTime()).toBe(24 * 60 * 60 * 1000);
    expect(dayBoundary(undefined, "from")).toBeUndefined();
    expect(dayBoundary("04/10/2026", "to")).toBeUndefined();
  });
});
