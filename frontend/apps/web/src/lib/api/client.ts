import { createApiClient, networkError, type ApiClient } from "@auxilia/api-client";

import type { BffArea } from "@/lib/bff/config";
import { CSRF_HEADER, CSRF_HEADER_VALUE } from "@/lib/bff/csrf";

const API_PREFIX = "/api/v1/";

/** BFF route of each area in the browser. */
export const BFF_PREFIX: Record<BffArea, string> = {
  tenant: "/api/bff/",
  platform: "/api/platform-bff/",
};

const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

export type BffClientOptions = {
  /**
   * Tenant app without a session (login, activation): the tenant of the page. Console: the tenant whose technical
   * endpoints are called (the BFF then uses a tenant-scoped platform token).
   */
  tenant?: string;
  /** Origin of the app; relative (same origin) in the browser, absolute in tests. */
  baseUrl?: string;
  /** For tests; defaults to the global fetch. */
  fetch?: (input: Request) => Promise<Response>;
};

/**
 * Typed client for browser code (skill auxilia-frontend-feature): paths and bodies come from the OpenAPI contract
 * (`/api/v1/...`), requests go to the BFF of the area with the session cookie, mutations carry the CSRF header, and
 * network failures become `ApiError` (status 0). Use with `unwrap()` inside React Query functions.
 */
export function createBffClient(area: BffArea, options: BffClientOptions = {}): ApiClient {
  const client = createApiClient({
    baseUrl: options.baseUrl ?? "",
    fetch: options.fetch,
    credentials: "same-origin",
  });
  client.use({
    onRequest({ request }) {
      const url = new URL(request.url, "http://relative.invalid");
      if (!url.pathname.startsWith(API_PREFIX)) {
        throw new Error(`Not an API path: ${url.pathname}`);
      }

      const target = BFF_PREFIX[area] + url.pathname.slice(API_PREFIX.length) + url.search;
      const absolute =
        url.origin === "http://relative.invalid" ? target : new URL(target, url.origin).toString();
      const rewritten = new Request(absolute, request);
      if (!SAFE_METHODS.has(rewritten.method)) {
        rewritten.headers.set(CSRF_HEADER, CSRF_HEADER_VALUE);
      }

      if (options.tenant) {
        rewritten.headers.set("x-tenant", options.tenant);
      }

      return rewritten;
    },
    onError({ error }) {
      return networkError(error);
    },
  });
  return client;
}
