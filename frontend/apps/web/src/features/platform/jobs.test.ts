import { describe, expect, it } from "vitest";

import enBundle from "../../../messages/en.json";
import itBundle from "../../../messages/it.json";
import { jobKey, jobsKey } from "./jobs-api";
import { tenantKey } from "./tenant-api";

const lookup = (bundle: Record<string, unknown>, key: string) => bundle[key];

/** The jobs registered by the backend today (IRecurringJob codes). */
const JOB_CODES = ["bus.outbox", "cases.expiry"];

describe("recurring jobs page", () => {
  it("turns a dotted job code into one translation segment", () => {
    expect(jobKey("cases.expiry")).toBe("casesExpiry");
    expect(jobKey("bus.outbox")).toBe("busOutbox");
    expect(jobKey("plain")).toBe("plain");
  });

  it("keys jobs and runs under the tenant, so 'run now' refreshes both", () => {
    const prefix = tenantKey("acme", "jobs");
    expect(jobsKey("acme", "list").slice(0, prefix.length)).toEqual([...prefix]);
    expect(jobsKey("acme", "runs").slice(0, prefix.length)).toEqual([...prefix]);
    expect(jobsKey("acme", "list")).not.toEqual(jobsKey("beta", "list"));
  });

  it("names, describes and schedules every registered job in English and Italian", () => {
    const keys = [
      "app.platform.nav.jobs",
      "app.platform.jobs.title",
      ...["Running", "Succeeded", "Failed"].map((status) => `app.platform.jobs.status.${status}`),
      ...JOB_CODES.flatMap((code) =>
        ["name", "description", "frequency"].map(
          (part) => `app.platform.jobs.catalog.${jobKey(code)}.${part}`,
        ),
      ),
    ];
    for (const key of keys) {
      expect(typeof lookup(enBundle, key), key).toBe("string");
      expect(typeof lookup(itBundle, key), key).toBe("string");
    }
  });
});
