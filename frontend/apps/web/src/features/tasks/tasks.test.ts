import { describe, expect, it } from "vitest";

import { missingItems } from "../cases/components/case-checklist";
import { moved } from "../services/components/checklist-editor";

import { taskBody, type TimelineEntry } from "./api";
import { entryIcon } from "./components/client-timeline";
import { activitySchema, taskSchema } from "./schemas/task";

const entry = (kind: string, titleKey: string): TimelineEntry => ({
  kind,
  at: "2026-10-04T10:00:00Z",
  titleKey,
  parameters: {},
  text: null,
  actorName: null,
  link: null,
  activityId: null,
  canDelete: false,
});

describe("tasks", () => {
  it("sends empty notes, dates and links as null", () => {
    expect(
      taskBody({
        title: " Call ",
        notes: "  ",
        dueOn: "",
        assigneeUserId: "u1",
        clientId: "",
        caseId: undefined,
      }),
    ).toEqual({
      title: "Call",
      notes: null,
      dueOn: null,
      assigneeUserId: "u1",
      clientId: null,
      caseId: null,
    });
    expect(
      taskBody({
        title: "x",
        notes: "n",
        dueOn: "2026-10-05",
        assigneeUserId: "u1",
        clientId: "c1",
      }).dueOn,
    ).toBe("2026-10-05");
  });

  it("checks titles, assignees and activities like the API", () => {
    expect(
      taskSchema.safeParse({ title: " ", notes: "", dueOn: "", assigneeUserId: "" }).success,
    ).toBe(false);
    expect(
      taskSchema.safeParse({ title: "Call", notes: "", dueOn: "", assigneeUserId: "u1" }).success,
    ).toBe(true);
    expect(activitySchema.safeParse({ kind: "Fax", text: "x" }).success).toBe(false);
  });

  it("gives every timeline entry an icon by kind", () => {
    expect(entryIcon(entry("activity", "app.timeline.activity.Call"))).not.toBe(
      entryIcon(entry("activity", "app.timeline.activity.Note")),
    );
    expect(entryIcon(entry("case.payment", "app.timeline.case.payment"))).toBeDefined();
  });

  it("moves checklist rows and finds the missing documents", () => {
    expect(moved(["a", "b", "c"], 0, 1)).toEqual(["b", "a", "c"]);
    expect(moved(["a", "b"], 0, -1)).toEqual(["a", "b"]);
    const item = (required: boolean, checkedAt: string | null) => ({
      itemId: `${required}-${checkedAt}`,
      name: "x",
      folderId: null,
      required,
      checkedAt,
      checkedBy: null,
    });
    expect(
      missingItems({ checklist: [item(true, null), item(true, "2026-10-04"), item(false, null)] }),
    ).toHaveLength(1);
  });
});
