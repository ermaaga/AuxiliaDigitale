import { describe, expect, it } from "vitest";

import { queryKey } from "@/lib/api/query-keys";
import { staleKeys } from "@/lib/realtime/realtime";

import { minutesBetween } from "../sessions/api";
import { requestSchema, replySchema } from "../requests/schemas/request";
import { notificationKeys, notificationsKey, notificationValues } from "./api";

describe("notifications", () => {
  it("renders the kind's keys with string placeholders", () => {
    expect(notificationKeys("appointment.approved")).toEqual({
      title: "notifications.appointment.approved.title",
      message: "notifications.appointment.approved.message",
    });
    expect(notificationValues({ when: "01/10/2026 09:00", count: 2, none: null })).toEqual({
      when: "01/10/2026 09:00",
      count: "2",
      none: "",
    });
    expect(notificationValues(null)).toEqual({});
  });

  it("realtime events refresh the queries of their module", () => {
    expect(staleKeys("demo", "NotificationReceived")).toEqual([notificationsKey("demo")]);
    expect(staleKeys("demo", "AppointmentChanged")).toEqual([
      queryKey("demo", "scheduling", "appointments"),
    ]);
    expect(staleKeys("demo", "RequestChanged")).toEqual([
      queryKey("demo", "engagement", "requests"),
    ]);
    expect(staleKeys("demo", "DocumentProcessed")).toEqual([queryKey("demo", "documents")]);
  });
});

describe("requests", () => {
  it("mirror the API limits", () => {
    const valid = {
      type: "Support",
      subject: " Invoice ",
      message: " Where is it? ",
      askMyOperator: true,
    } as const;
    expect(requestSchema.parse(valid)).toMatchObject({
      subject: "Invoice",
      message: "Where is it?",
    });
    const issues = requestSchema
      .safeParse({ ...valid, type: "Urgent", subject: "x".repeat(201), message: " " })
      .error?.issues.map((issue) => issue.message);
    expect(issues).toEqual([
      "validation.requests.type",
      "validation.requests.subject",
      "validation.requests.message",
    ]);
    expect(replySchema.safeParse({ message: "x".repeat(4001) }).success).toBe(false);
  });
});

describe("sessions", () => {
  it("count the minutes of a session", () => {
    expect(minutesBetween("2026-10-04T08:00:00Z", new Date("2026-10-04T09:30:00Z"))).toBe(90);
    expect(minutesBetween("2026-10-04T10:00:00Z", new Date("2026-10-04T09:30:00Z"))).toBe(0);
  });
});
