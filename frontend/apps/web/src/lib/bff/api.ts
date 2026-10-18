import type { BffArea, BffConfig } from "./config";

/** Request headers the browser may pass through to the API. */
const FORWARDED_REQUEST_HEADERS = [
  "accept",
  "accept-language",
  "content-type",
  "if-match",
  "if-none-match",
  "idempotency-key",
];

/** Response headers the browser may see (never Set-Cookie or server details). */
const FORWARDED_RESPONSE_HEADERS = [
  "cache-control",
  "content-disposition",
  "content-security-policy",
  "content-type",
  "etag",
  "location",
  "retry-after",
  "vary",
];

/** The CSP of the API's own responses (`SecurityHeaders.ApiContentSecurityPolicy`), kept when the API sends none. */
export const API_RESPONSE_CSP = "default-src 'none'; frame-ancestors 'none'";

export type ApiCall = {
  method: string;
  /** API path under `/api/v1`, without leading slash (`me/navigation`). */
  path: string;
  search?: string;
  body?: BodyInit | null;
  headers?: HeadersInit;
  accessToken?: string;
  tenant?: string;
  /** Sends the client secret too: only for the token endpoints. */
  withClientSecret?: boolean;
  /** Aborts the call (e.g. `AbortSignal.timeout(...)`). */
  signal?: AbortSignal;
};

/**
 * Calls the API as the client application of the area. The caller's IP and user agent are forwarded
 * (`X-Forwarded-For`, trusted by the API only from known proxies) so rate limits and the login audit see the user.
 */
export async function callApi(
  config: BffConfig,
  area: BffArea,
  call: ApiCall,
  incoming?: Request,
): Promise<Response> {
  const client = config.clients[area];
  const headers = new Headers(call.headers);
  headers.set("x-client-id", client.id);
  if (call.withClientSecret && client.secret) {
    headers.set("x-client-secret", client.secret);
  }

  if (call.accessToken) {
    headers.set("authorization", `Bearer ${call.accessToken}`);
  }

  if (call.tenant) {
    headers.set("x-tenant", call.tenant);
  }

  if (incoming) {
    const ip = clientIp(incoming);
    if (ip) {
      headers.set("x-forwarded-for", ip);
    }

    const agent = incoming.headers.get("user-agent");
    if (agent) {
      headers.set("user-agent", agent);
    }
  }

  return fetch(`${config.apiUrl}/api/v1/${call.path}${call.search ?? ""}`, {
    method: call.method,
    headers,
    body: call.body,
    cache: "no-store",
    redirect: "manual",
    signal: call.signal,
  });
}

/** Headers of the browser request that are forwarded to the API. */
export function forwardableRequestHeaders(request: Request): Headers {
  const headers = new Headers();
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = request.headers.get(name);
    if (value !== null) {
      headers.set(name, value);
    }
  }

  return headers;
}

/** The API response for the browser: status, body and the allow-listed headers only. */
export function toBrowserResponse(response: Response): Response {
  const headers = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = response.headers.get(name);
    if (value !== null) {
      headers.set(name, value);
    }
  }

  if (!headers.has("cache-control")) {
    headers.set("cache-control", "no-store");
  }

  // A file opened in a tab (PDF preview) is a document of the app's origin: it keeps the API's CSP, which lets it
  // run nothing and be framed nowhere (the pages' CSP of src/proxy.ts does not cover /api).
  if (!headers.has("content-security-policy")) {
    headers.set("content-security-policy", API_RESPONSE_CSP);
  }

  const noBody = response.status === 204 || response.status === 304;
  return new Response(noBody ? null : response.body, { status: response.status, headers });
}

/**
 * The caller's address as appended by the hosting reverse proxy: the last `X-Forwarded-For` entry (earlier entries
 * can be forged by the client). Without a proxy in front of Next.js there is none and the API sees the BFF address.
 */
function clientIp(request: Request): string | undefined {
  const entries = request.headers
    .get("x-forwarded-for")
    ?.split(",")
    .map((entry) => entry.trim())
    .filter(Boolean);
  return entries?.at(-1) || request.headers.get("x-real-ip") || undefined;
}
