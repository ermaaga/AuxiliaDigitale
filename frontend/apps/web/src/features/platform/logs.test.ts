import { describe, expect, it } from "vitest";

import enBundle from "../../../messages/en.json";
import itBundle from "../../../messages/it.json";
import {
  DEBUG_DURATIONS,
  LOG_FILTERS,
  LOG_LEVELS,
  LOG_PAGE_SIZE,
  debugUntil,
  levelVariant,
  logQuery,
  logsKey,
} from "./logs-api";
import { tenantKey } from "./tenant-api";

/** The static bundles are flat: dotted keys (`app.platform.logs.title`). */
const lookup = (bundle: Record<string, unknown>, key: string) => bundle[key];

describe("tenant logs", () => {
  it("sends only the filters that are set, with the page size and the cursor", () => {
    expect(logQuery({})).toEqual({ pageSize: LOG_PAGE_SIZE });
    expect(
      logQuery(
        { from: "2026-10-01", to: "2026-10-02", level: "Warning", code: " AUX-11030 ", text: "  " },
        "20261002:12",
      ),
    ).toEqual({
      pageSize: LOG_PAGE_SIZE,
      from: "2026-10-01",
      to: "2026-10-02",
      level: "Warning",
      code: "AUX-11030",
      cursor: "20261002:12",
    });
    expect(logQuery({ traceId: "abc", userId: "u-1" }, null)).toEqual({
      pageSize: LOG_PAGE_SIZE,
      traceId: "abc",
      userId: "u-1",
    });
  });

  it("ends debug logging after the chosen duration, never beyond the 24 hours of the API", () => {
    const now = new Date("2026-10-02T10:00:00Z");
    expect(debugUntil(now, 30)).toBe("2026-10-02T10:30:00.000Z");
    expect(Math.max(...DEBUG_DURATIONS.map((duration) => duration.minutes))).toBeLessThanOrEqual(
      24 * 60,
    );
  });

  it("makes errors stand out and debug detail recede", () => {
    expect(levelVariant("Fatal")).toBe("destructive");
    expect(levelVariant("Error")).toBe("destructive");
    expect(levelVariant("Warning")).toBe("default");
    expect(levelVariant("Information")).toBe("secondary");
    expect(levelVariant("Debug")).toBe("outline");
    expect(LOG_LEVELS[0]).toBe("Verbose");
  });

  it("keys level and events under the tenant, so a level change refreshes both", () => {
    const prefix = tenantKey("acme", "logs");
    expect(logsKey("acme", "level").slice(0, prefix.length)).toEqual([...prefix]);
    expect(logsKey("acme", "events", { level: "Error" }).slice(0, prefix.length)).toEqual([
      ...prefix,
    ]);
    expect(logsKey("acme", "events", { level: "Error" })).not.toEqual(
      logsKey("beta", "events", { level: "Error" }),
    );
  });

  it("ships every text of the page in English and Italian", () => {
    const keys = [
      ...DEBUG_DURATIONS.map((duration) => `app.platform.logs.${duration.key}`),
      ...LOG_FILTERS.filter((name) => name !== "level").map((name) => `app.platform.logs.${name}`),
      "app.platform.logs.minimumLevel",
      "app.platform.nav.logs",
      "validation.logs.until",
      "validation.logs.range",
      "errors.AUX-11033",
    ];
    for (const key of keys) {
      expect(typeof lookup(enBundle, key), key).toBe("string");
      expect(typeof lookup(itBundle, key), key).toBe("string");
    }
  });
});
