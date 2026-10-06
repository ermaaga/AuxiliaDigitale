import { createApiClient, networkError, type ApiClient } from "@auxilia/api-client";

import type { BffArea } from "@/lib/bff/config";
import { CSRF_HEADER, CSRF_HEADER_VALUE } from "@/lib/bff/csrf";

import { isVersionedWrite, resourceVersions, versionKey } from "./resource-versions";

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
  const scope = `${area}:${options.tenant ?? ""}`;
  const send = options.fetch ?? ((input: Request) => fetch(input));
  client.use({
    async onRequest({ request }) {
      const url = new URL(request.url, "http://relative.invalid");
      if (!url.pathname.startsWith(API_PREFIX)) {
        throw new Error(`Not an API path: ${url.pathname}`);
      }

      const target = BFF_PREFIX[area] + url.pathname.slice(API_PREFIX.length) + url.search;
      const absolute =
        url.origin === "http://relative.invalid" ? target : new URL(target, url.origin).toString();
      const safe = SAFE_METHODS.has(request.method);
      const headers = new Headers(request.headers);
      if (!safe) {
        headers.set(CSRF_HEADER, CSRF_HEADER_VALUE);
      }

      if (options.tenant) {
        headers.set("x-tenant", options.tenant);
      }

      // F29: a write of a shared resource carries the version the caller read (read now if it never was).
      if (isVersionedWrite(request.method, url.pathname) && !headers.has("if-match")) {
        const key = versionKey(scope, url.pathname);
        const etag =
          resourceVersions.get(key) ??
          (await readVersion(send, absolute.replace(/\?.*$/, ""), options.tenant, request.signal));
        if (etag) {
          headers.set("if-match", etag);
        }
      }

      // The body is buffered, never passed on as the original request's stream: browsers send a stream body as a
      // streaming upload, which needs HTTP/2 and fails over HTTP/1.1 (Chrome: ERR_ALPN_NEGOTIATION_FAILED).
      return new Request(absolute, {
        method: request.method,
        headers,
        body: safe ? undefined : await request.arrayBuffer(),
        credentials: request.credentials,
        signal: request.signal,
      });
    },
    onResponse({ request, response }) {
      const apiPath =
        API_PREFIX +
        new URL(request.url, "http://relative.invalid").pathname.slice(BFF_PREFIX[area].length);
      const key = versionKey(scope, apiPath);
      const etag = response.headers.get("etag");
      if (request.method === "GET" && response.ok && etag) {
        resourceVersions.set(key, etag);
      } else if (
        isVersionedWrite(request.method, apiPath) &&
        (response.ok || response.status === 412)
      ) {
        resourceVersions.forget(key);
      }

      return response;
    },
    onError({ error }) {
      return networkError(error);
    },
  });
  return client;
}

/** The current ETag of a resource through the BFF; undefined when it cannot be read (the write then reports why). */
async function readVersion(
  send: (input: Request) => Promise<Response>,
  url: string,
  tenant: string | undefined,
  signal: AbortSignal,
): Promise<string | undefined> {
  const headers = new Headers();
  if (tenant) {
    headers.set("x-tenant", tenant);
  }

  const response = await send(
    new Request(url, { method: "GET", headers, credentials: "same-origin", signal }),
  );
  return response.ok ? (response.headers.get("etag") ?? undefined) : undefined;
}
