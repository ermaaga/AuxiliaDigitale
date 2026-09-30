import { HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";

import { TEST_API_URL, apiHandler, apiOk, apiProblem, mswServer, useMsw } from "@/test/msw";

import { buildBundles } from "../../scripts/sync-messages.mjs";
import enBundle from "../../messages/en.json";
import itBundle from "../../messages/it.json";
import { clearBundleCache, tenantBundle, tenantLanguages } from "./bundles";
import { resolveLocale } from "./locale";
import { fallbackMessages, unflatten } from "./messages";
import { tenantFromPath } from "./tenant";

describe("static fallback bundles", () => {
  it("are in sync with the shipped translations of the backend (run pnpm i18n:sync)", async () => {
    const bundles = await buildBundles();
    expect(enBundle).toEqual(bundles.en);
    expect(itBundle).toEqual(bundles.it);
  });

  it("have EN and IT for the same keys, including the web app keys", () => {
    expect(Object.keys(itBundle)).toEqual(Object.keys(enBundle));
    expect(fallbackMessages("it")["errors.AUX-WEB-NETWORK"]).toBe(
      "Nessuna connessione. Controlla la rete e riprova.",
    );
    expect(fallbackMessages("de")).toBe(enBundle);
  });

  it("can be nested for next-intl without losing keys", () => {
    const nested = unflatten(itBundle) as Record<string, Record<string, unknown>>;
    expect(nested.nav?.cases).toBe("Pratiche");
    expect((nested.errors as Record<string, unknown>)["AUX-WEB-401"]).toBeTypeOf("string");
    expect(nested.Save).toBe("Salva");
    const count = (node: unknown): number =>
      typeof node === "string"
        ? 1
        : Object.values(node as object).reduce((sum: number, child) => sum + count(child), 0);
    expect(count(nested)).toBe(Object.keys(itBundle).length);
  });
});

describe("unflatten", () => {
  it("nests dotted keys and keeps the namespace when a key is also a prefix", () => {
    expect(unflatten({ "a.b": "1", "a.c.d": "2", e: "3" })).toEqual({
      a: { b: "1", c: { d: "2" } },
      e: "3",
    });
    expect(unflatten({ a: "leaf", "a.b": "nested" })).toEqual({ a: { b: "nested" } });
  });
});

describe("resolveLocale", () => {
  const available = ["it", "en", "pt-BR"];

  it("prefers the user's choice, then the browser, then the tenant default", () => {
    expect(
      resolveLocale({ preferred: "en", acceptLanguage: "it", available, fallback: "it" }),
    ).toBe("en");
    expect(
      resolveLocale({
        preferred: "de",
        acceptLanguage: "de-DE, en;q=0.8, it;q=0.9",
        available,
        fallback: "it",
      }),
    ).toBe("it");
    expect(resolveLocale({ acceptLanguage: "pt-br,pt;q=0.9", available })).toBe("pt-BR");
    expect(resolveLocale({ acceptLanguage: "en-GB", available })).toBe("en");
    expect(
      resolveLocale({ acceptLanguage: "fr, *;q=0.5, en;q=0", available, fallback: "it" }),
    ).toBe("it");
    expect(resolveLocale({ available: ["en"], fallback: "it" })).toBe("en");
    expect(resolveLocale({ available: [] })).toBe("it");
  });
});

describe("tenantFromPath", () => {
  it("reads the first segment unless it is an app route", () => {
    expect(tenantFromPath("/acme/cases")).toBe("acme");
    expect(tenantFromPath("/studio-b")).toBe("studio-b");
    expect(tenantFromPath("/platform/tenants")).toBeUndefined();
    expect(tenantFromPath("/design-system")).toBeUndefined();
    expect(tenantFromPath("/")).toBeUndefined();
    expect(tenantFromPath("/Bad_Slug/x")).toBeUndefined();
  });
});

describe("tenant bundles", () => {
  useMsw();

  let calls: Request[];
  let replies: Array<() => Response>;

  beforeEach(() => {
    clearBundleCache();
    calls = [];
    replies = [];
    // Every call of the test gets the next prepared answer; none left → network failure.
    const answer = ({ request }: { request: Request }) => {
      calls.push(request.clone());
      return replies.shift()?.() ?? HttpResponse.error();
    };
    mswServer.use(
      apiHandler("get", "/api/v1/i18n/languages", answer),
      apiHandler("get", "/api/v1/i18n/{language}", answer),
    );
  });

  const bundle = (body: Record<string, string>, etag: string) => () =>
    apiOk("get", "/api/v1/i18n/{language}", body, { headers: { etag } });

  it("loads anonymously with the tenant, then revalidates with the ETag", async () => {
    replies.push(
      bundle({ Save: "Salva" }, '"v1"'),
      () => new Response(null, { status: 304 }),
      bundle({ Save: "Memorizza" }, '"v2"'),
    );

    expect(await tenantBundle("acme", "it", 0)).toEqual({ Save: "Salva" });
    expect(await tenantBundle("acme", "it", 5_000)).toEqual({ Save: "Salva" });
    expect(calls).toHaveLength(1);
    expect(calls[0]!.url).toBe(`${TEST_API_URL}/api/v1/i18n/it`);
    expect(calls[0]!.headers.get("x-tenant")).toBe("acme");
    expect(calls[0]!.headers.get("authorization")).toBeNull();

    expect(await tenantBundle("acme", "it", 11_000)).toEqual({ Save: "Salva" });
    expect(calls[1]!.headers.get("if-none-match")).toBe('"v1"');

    expect(await tenantBundle("acme", "it", 22_000)).toEqual({ Save: "Memorizza" });
    expect(await tenantBundle("other", "it", 22_000)).toBeUndefined();
  });

  it("keeps the last good bundle when the API fails and has none before the first success", async () => {
    expect(await tenantBundle("acme", "it", 0)).toBeUndefined();

    replies.push(bundle({ Save: "Salva" }, '"v1"'), () => apiProblem(500, "AUX-10001"));
    expect(await tenantBundle("acme", "it", 20_000)).toEqual({ Save: "Salva" });
    expect(await tenantBundle("acme", "it", 40_000)).toEqual({ Save: "Salva" });
    expect(await tenantBundle("acme", "it", 60_000)).toEqual({ Save: "Salva" });
  });

  it("caches the tenant languages for a minute", async () => {
    replies.push(() =>
      apiOk("get", "/api/v1/i18n/languages", [{ code: "it", name: "Italiano", isDefault: true }]),
    );

    expect(await tenantLanguages("acme", 0)).toEqual([
      { code: "it", name: "Italiano", isDefault: true },
    ]);
    expect(await tenantLanguages("acme", 30_000)).toHaveLength(1);
    expect(calls).toHaveLength(1);
    expect(await tenantLanguages("acme", 61_000)).toHaveLength(1);
    expect(await tenantLanguages("unknown", 0)).toBeUndefined();
  });
});
