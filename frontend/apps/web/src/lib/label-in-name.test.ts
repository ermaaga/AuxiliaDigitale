import { describe, expect, it } from "vitest";

import { labelInName } from "./label-in-name";

describe("labelInName", () => {
  it("keeps a label that already contains the visible words in order", () => {
    expect(labelInName("Riapri", "Riapri Attività A")).toBe("Riapri Attività A");
    expect(labelInName("Termina sessione", "Termina sessione di Mario")).toBe(
      "Termina sessione di Mario",
    );
    expect(labelInName("Esegui ora", "Esegui ora Scadenze")).toBe("Esegui ora Scadenze");
  });

  it("ignores case and punctuation like axe", () => {
    expect(labelInName("Edit", "edit: tag A")).toBe("edit: tag A");
    expect(labelInName("Ripristina predefiniti", "Ripristina (predefiniti)")).toBe(
      "Ripristina (predefiniti)",
    );
  });

  it("puts the visible text first when the label does not contain it", () => {
    expect(labelInName("Open again", "Open Task A again")).toBe("Open again: Open Task A again");
    expect(labelInName("Edit", "Rename Tag A")).toBe("Edit: Rename Tag A");
    expect(labelInName("End session", "End the session of Mario")).toBe(
      "End session: End the session of Mario",
    );
  });

  it("does not match a visible word inside a longer word", () => {
    expect(labelInName("Run", "Rerun the job")).toBe("Run: Rerun the job");
  });

  it("keeps the label of a control without visible words", () => {
    expect(labelInName("", "Notifications")).toBe("Notifications");
  });
});
