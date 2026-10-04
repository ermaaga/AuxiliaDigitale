import { describe, expect, it } from "vitest";

import { tagColor } from "./components/client-consents-tags";

describe("tags", () => {
  it("accepts #rrggbb colours, empty means none", () => {
    expect(tagColor(" #72fa29 ")).toBe("#72fa29");
    expect(tagColor("")).toBeNull();
    expect(tagColor("red")).toBeUndefined();
    expect(tagColor("#12345")).toBeUndefined();
  });
});
