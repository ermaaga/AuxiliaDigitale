import { renderToStaticMarkup } from "react-dom/server";
import { ApiError, isApiError, networkError, unwrap } from "@auxilia/api-client";
import { describe, expect, it, vi } from "vitest";

import { ApiErrorAlert, errorMessage } from "@/components/errors/api-error-alert";
import { createBffClient } from "./client";
import { createQueryClient, shouldRetry } from "./query-client";
import { newIdempotencyKey, queryKey } from "./query-keys";

const ORIGIN = "http://app.test";

function recordingFetch(reply: () => Response | Promise<Response>) {
  const requests: Request[] = [];
  const fetch = vi.fn(async (request: Request) => {
    requests.push(request);
    return reply();
  });
  return { fetch, requests };
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": status < 400 ? "application/json" : "application/problem+json" },
  });

describe("BFF client", () => {
  it("sends typed API paths to the tenant BFF, reads GETs without the CSRF header", async () => {
    const { fetch, requests } = recordingFetch(() => json(200, { Save: "Salva" }));
    const client = createBffClient("tenant", { baseUrl: ORIGIN, fetch, tenant: "acme" });

    const bundle = unwrap(
      await client.GET("/api/v1/i18n/{language}", { params: { path: { language: "it" } } }),
    );

    expect(bundle).toEqual({ Save: "Salva" });
    expect(requests[0]!.url).toBe(`${ORIGIN}/api/bff/i18n/it`);
    expect(requests[0]!.headers.get("x-tenant")).toBe("acme");
    expect(requests[0]!.headers.get("x-requested-with")).toBeNull();
  });

  it("adds the CSRF header to mutations, keeps query and body, and routes the console to its BFF", async () => {
    const { fetch, requests } = recordingFetch(() => json(201, { id: "0199" }));
    const client = createBffClient("platform", { baseUrl: ORIGIN, fetch, tenant: "acme" });

    await client.POST("/api/v1/localization/keys", {
      body: { key: "app.x", category: "app", description: null, translations: null },
    });
    await client.GET("/api/v1/localization/keys", {
      params: { query: { search: "save", page: 2 } },
    });

    expect(requests[0]!.url).toBe(`${ORIGIN}/api/platform-bff/localization/keys`);
    expect(requests[0]!.method).toBe("POST");
    expect(requests[0]!.headers.get("x-requested-with")).toBe("auxilia");
    expect(await requests[0]!.json()).toMatchObject({ key: "app.x" });
    expect(requests[1]!.url).toBe(
      `${ORIGIN}/api/platform-bff/localization/keys?search=save&page=2`,
    );
  });

  it("turns ProblemDetails into ApiError and network failures into status 0", async () => {
    const failing = createBffClient("tenant", {
      baseUrl: ORIGIN,
      fetch: recordingFetch(() =>
        json(404, {
          title: "Language de is not available",
          errorCode: "AUX-21003",
          traceId: "00-t-01",
        }),
      ).fetch,
    });
    const offline = createBffClient("tenant", {
      baseUrl: ORIGIN,
      fetch: async () => {
        throw new TypeError("fetch failed");
      },
    });

    const error = await failing
      .GET("/api/v1/i18n/{language}", { params: { path: { language: "de" } } })
      .then(unwrap, (e: unknown) => e)
      .catch((e: unknown) => e);
    expect(isApiError(error) && [error.status, error.errorCode, error.traceId]).toEqual([
      404,
      "AUX-21003",
      "00-t-01",
    ]);

    const network = await offline.GET("/api/v1/me").catch((e: unknown) => e);
    expect(isApiError(network) && [network.status, network.errorCode]).toEqual([
      0,
      "AUX-WEB-NETWORK",
    ]);
  });
});

describe("React Query defaults", () => {
  it("retries transient failures twice and nothing else", () => {
    expect(shouldRetry(0, networkError(new Error("x")))).toBe(true);
    expect(shouldRetry(1, new ApiError(503, undefined))).toBe(true);
    expect(shouldRetry(2, new ApiError(503, undefined))).toBe(false);
    expect(shouldRetry(0, new ApiError(404, undefined))).toBe(false);
    expect(shouldRetry(0, new Error("bug"))).toBe(false);
  });

  it("reports an ended session once per failing query or mutation", async () => {
    const onUnauthenticated = vi.fn();
    const client = createQueryClient(onUnauthenticated);

    await client
      .fetchQuery({
        queryKey: ["acme", "me"],
        queryFn: () => Promise.reject(new ApiError(401, { errorCode: "AUX-WEB-401" })),
      })
      .catch(() => undefined);
    await client
      .fetchQuery({
        queryKey: ["acme", "x"],
        queryFn: () => Promise.reject(new ApiError(403, undefined)),
      })
      .catch(() => undefined);
    await client
      .getMutationCache()
      .build(client, { mutationFn: () => Promise.reject(new ApiError(401, undefined)) })
      .execute(undefined)
      .catch(() => undefined);

    expect(onUnauthenticated).toHaveBeenCalledTimes(2);
    expect(client.getDefaultOptions().mutations?.retry).toBe(false);
  });

  it("builds tenant-scoped query keys and idempotency keys", () => {
    expect(queryKey("acme", "cases")).toEqual(["acme", "cases"]);
    expect(queryKey("acme", "cases", "list", { page: 1 })).toEqual([
      "acme",
      "cases",
      "list",
      { page: 1 },
    ]);
    expect(newIdempotencyKey()).toMatch(/^[0-9a-f-]{36}$/);
    expect(newIdempotencyKey()).not.toBe(newIdempotencyKey());
  });
});

describe("error message and alert", () => {
  const translations: Record<string, string> = {
    "errors.AUX-21003": "Lingua non disponibile",
    "errors.generic": "Si è verificato un errore",
  };
  const translate = (key: string) => translations[key];
  const labels = { title: "Errore", retry: "Riprova", copy: "Copia il codice", trace: "Traccia" };

  it("translates the AUX code and falls back to the generic message", () => {
    expect(errorMessage(new ApiError(404, { errorCode: "AUX-21003" }), translate)).toBe(
      "Lingua non disponibile",
    );
    expect(errorMessage(new ApiError(500, { errorCode: "AUX-10000" }), translate)).toBe(
      "Si è verificato un errore",
    );
    expect(errorMessage(new Error("bug"), translate)).toBe("Si è verificato un errore");
    expect(errorMessage(new Error("bug"), () => undefined)).toBe("errors.generic");
  });

  it("shows message, code, trace id and retry", () => {
    const html = renderToStaticMarkup(
      <ApiErrorAlert
        error={new ApiError(404, { errorCode: "AUX-21003", traceId: "00-t-01" })}
        translate={translate}
        labels={labels}
        onRetry={() => undefined}
      />,
    );

    expect(html).toContain('role="alert"');
    expect(html).toContain("Lingua non disponibile");
    expect(html).toContain("AUX-21003");
    expect(html).toContain("00-t-01");
    expect(html).toContain("Riprova");
    expect(html).toContain('aria-label="Copia il codice"');
    expect(
      renderToStaticMarkup(
        <ApiErrorAlert error={new Error("x")} translate={translate} labels={labels} />,
      ),
    ).not.toContain("Riprova");
  });
});
