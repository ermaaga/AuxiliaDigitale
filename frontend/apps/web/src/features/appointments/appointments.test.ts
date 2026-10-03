import { describe, expect, it } from "vitest";

import { queryKey } from "@/lib/api/query-keys";

import { appointmentsKey, isOpen } from "./api";
import {
  addDays,
  apiTime,
  appointmentSchema,
  fromWallClock,
  nowIn,
  requestSchema,
  shortTime,
  wallClock,
} from "./schemas/appointment";

const valid = {
  clientId: "client",
  employeeUserId: "",
  date: "2026-10-05",
  time: "09:30",
  durationMinutes: " 45 ",
  notes: "",
  showInGlobalCalendar: true,
};

describe("appointment schemas (mirror the API limits)", () => {
  it("accepts a slot and checks client, duration and notes", () => {
    expect(appointmentSchema.parse(valid).durationMinutes).toBe("45");
    const issues = appointmentSchema
      .safeParse({ ...valid, clientId: "", durationMinutes: "4", notes: "x".repeat(1001) })
      .error?.issues.map((issue) => issue.message);
    expect(issues).toEqual([
      "validation.appointments.client",
      "validation.appointments.duration",
      "validation.appointments.notes",
    ]);
    expect(appointmentSchema.safeParse({ ...valid, durationMinutes: "1440" }).success).toBe(true);
    expect(appointmentSchema.safeParse({ ...valid, durationMinutes: "1441" }).success).toBe(false);
  });

  it("requires an operator for a client request", () => {
    expect(
      requestSchema.safeParse({
        employeeUserId: "",
        date: "2026-10-05",
        time: "09:00",
        durationMinutes: "60",
        notes: "",
      }).error?.issues[0]?.message,
    ).toBe("validation.appointments.employee");
  });
});

describe("tenant wall-clock times", () => {
  it("converts API and form times", () => {
    expect(shortTime("09:30:00")).toBe("09:30");
    expect(apiTime("09:30")).toBe("09:30:00");
    expect(apiTime("09:30:00")).toBe("09:30:00");
  });

  it("writes the local start and end as UTC wall clock, across midnight too", () => {
    expect(wallClock("2026-10-05", "09:30:00")).toBe("2026-10-05T09:30:00");
    expect(wallClock("2026-10-05", "23:30:00", 45)).toBe("2026-10-06T00:15:00");
    expect(fromWallClock(new Date("2026-10-05T07:05:00Z"))).toEqual({
      date: "2026-10-05",
      time: "07:05",
    });
  });

  it("reads now in the tenant time zone and adds days", () => {
    // 22:30 UTC on 3 October is already 4 October in Rome (UTC+2).
    expect(nowIn("Europe/Rome", new Date("2026-10-03T22:30:00Z"))).toEqual({
      date: "2026-10-04",
      time: "00:30",
    });
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2026-03-01", -1)).toBe("2026-02-28");
  });
});

describe("appointment helpers", () => {
  it("knows the open statuses and keeps every query under one prefix", () => {
    expect(["Pending", "Approved", "Rejected", "Completed", "Cancelled"].filter(isOpen)).toEqual([
      "Pending",
      "Approved",
    ]);
    expect(appointmentsKey("demo")).toEqual(queryKey("demo", "scheduling", "appointments"));
    expect(appointmentsKey("demo", "detail", { id: "a" })).toEqual(
      queryKey("demo", "scheduling", "appointments", { entity: "detail", id: "a" }),
    );
  });
});
