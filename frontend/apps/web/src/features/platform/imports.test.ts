import { describe, expect, it } from "vitest";

import {
  IMPORT_JOB_STATUSES,
  IMPORT_ROW_STATUSES,
  isFinished,
  isRunning,
  statusVariant,
  templateFileName,
} from "./imports-api";

describe("imports", () => {
  it("refreshes while the Worker works and deletes only finished imports", () => {
    expect(IMPORT_JOB_STATUSES.filter(isRunning)).toEqual(["Pending", "Validating", "Processing"]);
    expect(IMPORT_JOB_STATUSES.filter(isFinished)).toEqual(["Completed", "Failed", "Cancelled"]);
  });

  it("shows errors in red and done work as default badges", () => {
    expect(IMPORT_ROW_STATUSES.map(statusVariant)).toEqual([
      "default",
      "destructive",
      "default",
      "destructive",
    ]);
    expect(statusVariant("AwaitingConfirmation")).toBe("secondary");
    expect(statusVariant("Processing")).toBe("outline");
  });

  it("names the template like the API", () => {
    expect(templateFileName("Clienti 2026")).toBe("Clienti_2026_template.xlsx");
    expect(templateFileName("Attività/è")).toBe("Attività_è_template.xlsx");
  });
});
