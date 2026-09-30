import { apiErrorFrom, networkError } from "@auxilia/api-client";

import { CSRF_HEADER, CSRF_HEADER_VALUE } from "@/lib/bff/csrf";

/**
 * POST to a route of the BFF itself (`/api/auth/login`, `/api/auth/logout`, …) with the CSRF header; resolves with the
 * JSON body (or `undefined`), throws `ApiError` on failure.
 */
export async function postToBff<T = unknown>(url: string, body?: unknown): Promise<T | undefined> {
  let response: Response;
  try {
    response = await fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        [CSRF_HEADER]: CSRF_HEADER_VALUE,
        ...(body === undefined ? {} : { "content-type": "application/json" }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (error) {
    throw networkError(error);
  }

  const text = await response.text();
  const json: unknown = text ? safeJson(text) : undefined;
  if (!response.ok) {
    throw apiErrorFrom(response, json);
  }

  return json as T | undefined;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
