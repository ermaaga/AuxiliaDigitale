import { describe, expect, it } from "vitest";

import { normalizeCode } from "./api";

describe("normalizeCode", () => {
  it("keeps the six digits whatever the app's display adds", () => {
    expect(normalizeCode("123 456")).toBe("123456");
    expect(normalizeCode(" 12-34-56 ")).toBe("123456");
    expect(normalizeCode("1234567")).toBe("123456");
  });
});
