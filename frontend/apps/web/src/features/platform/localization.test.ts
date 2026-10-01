import { describe, expect, it } from "vitest";

import { localizationKey, translationOf, type ResourceKey } from "./localization-api";
import { initialTranslations, keyDetailsSchema, resourceKeySchema } from "./schemas/localization";
import { tenantKey } from "./tenant-api";

describe("translation editor", () => {
  it("accepts keys and categories as the API does", () => {
    expect(
      resourceKeySchema.safeParse({ key: "app.cases.title", category: "app", description: "" })
        .success,
    ).toBe(true);
    expect(
      resourceKeySchema.safeParse({ key: "Save_As-2", category: "commonText", description: "" })
        .success,
    ).toBe(true);
    const invalid = resourceKeySchema.safeParse({
      key: "1abc",
      category: "App",
      description: "x".repeat(501),
    });
    expect(invalid.error?.issues.map((issue) => [issue.path[0], issue.message])).toEqual([
      ["key", "validation.localization.key"],
      ["category", "validation.localization.category"],
      ["description", "validation.maximumLength"],
    ]);
    expect(keyDetailsSchema.safeParse({ category: "app", description: "" }).success).toBe(true);
  });

  it("sends only the languages with a first translation", () => {
    expect(initialTranslations({ it: " Ciao ", en: "  " })).toEqual({ it: "Ciao" });
    expect(initialTranslations({ it: "", en: "" })).toBeNull();
  });

  it("finds a translation and nests its queries under the invalidated prefix", () => {
    const key = {
      translations: [{ languageCode: "it", value: "Salva", isCustomized: false }],
    } as unknown as ResourceKey;
    expect(translationOf(key, "it")?.value).toBe("Salva");
    expect(translationOf(key, "en")).toBeUndefined();

    const prefix = tenantKey("demo", "localization");
    expect(localizationKey("demo", "keys", { page: 2 }).slice(0, prefix.length)).toEqual([
      ...prefix,
    ]);
  });
});
