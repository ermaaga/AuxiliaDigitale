import { afterAll, afterEach, beforeAll, vi } from "vitest";
import type { paths } from "@auxilia/api-client";
import { http, HttpResponse, type HttpHandler } from "msw";
import { setupServer } from "msw/node";

/**
 * MSW for Vitest (skill auxilia-testing): handlers are declared on the API contract (`paths` of
 * `@auxilia/api-client`), so a test cannot mock an endpoint that does not exist or answer 200 with the wrong shape.
 * Server code calls the API at {@link TEST_API_URL}; any request without a handler fails the test.
 */
export const TEST_API_URL = "http://api.test";

type Method = "get" | "post" | "put" | "patch" | "delete";

/** API paths that have `method`. */
export type ApiPath<M extends Method> = {
  [P in keyof paths]: paths[P] extends Record<M, object> ? P : never;
}[keyof paths];

/** JSON body of the 200 answer of `method path` in the contract. */
export type ApiOkBody<M extends Method, P extends ApiPath<M>> = paths[P][M] extends {
  responses: { 200: { content: { "application/json": infer Body } } };
}
  ? Body
  : never;

type Resolver = (info: {
  request: Request;
  params: Record<string, string | readonly string[] | undefined>;
}) => Response | Promise<Response>;

/** `/api/v1/i18n/{language}` → `http://api.test/api/v1/i18n/:language`. */
export function mswPath(path: string): string {
  return TEST_API_URL + path.replace(/\{([^}]+)\}/g, ":$1");
}

/** A handler for `method path` of the API contract. */
export function apiHandler<M extends Method, P extends ApiPath<M>>(
  method: M,
  path: P,
  resolver: Resolver,
): HttpHandler {
  return http[method](mswPath(path), ({ request, params }) => resolver({ request, params }));
}

/** A 200 JSON answer typed on the contract of `method path`. */
export function apiOk<M extends Method, P extends ApiPath<M>>(
  _method: M,
  _path: P,
  body: ApiOkBody<M, P>,
  init?: { headers?: Record<string, string> },
): Response {
  return HttpResponse.json(body as never, { status: 200, headers: init?.headers });
}

/** An API error in the ProblemDetails shape of the API (`errorCode`, `traceId`, field `errors`). */
export function apiProblem(
  status: number,
  errorCode: string,
  errors?: Record<string, string[]>,
): Response {
  return HttpResponse.json(
    { status, title: errorCode, errorCode, traceId: "00-test-00", ...(errors ? { errors } : {}) },
    { status, headers: { "content-type": "application/problem+json" } },
  );
}

export const mswServer = setupServer();

/**
 * Starts MSW for the test file: the server code under test points at {@link TEST_API_URL}, handlers added with
 * `mswServer.use(...)` are reset after each test, unhandled requests are errors.
 */
export function useMsw() {
  beforeAll(() => {
    vi.stubEnv("AUXILIA_API_URL", TEST_API_URL);
    mswServer.listen({ onUnhandledFrame: "error" });
  });
  afterEach(() => mswServer.resetHandlers());
  afterAll(() => {
    mswServer.close();
    vi.unstubAllEnvs();
  });
}
