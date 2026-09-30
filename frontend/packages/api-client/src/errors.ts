import type { components } from "./schema";

/** RFC 9457 ProblemDetails of the API with the Auxilia extensions (skill auxilia-api-contract). */
export type Problem = components["schemas"]["ProblemDetails"] & {
  errorCode?: string | null;
  traceId?: string | null;
  errors?: Record<string, string[]> | null;
};

/** Error code of failures that never reached the API or returned no ProblemDetails. */
export const NETWORK_ERROR_CODE = "AUX-WEB-NETWORK";
export const UNKNOWN_ERROR_CODE = "AUX-WEB-UNKNOWN";

/**
 * A failed API call, as the UI shows it: HTTP status, the `AUX-NNNNN` code (the UI translates `errors.<code>`,
 * falling back to `errors.generic`), the English title for logs, the trace id to copy and the field errors
 * (translation keys per camelCase field) of a validation failure.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly errorCode: string;
  readonly traceId: string | undefined;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;

  constructor(status: number, problem: Problem | undefined, fallbackCode = UNKNOWN_ERROR_CODE) {
    super(problem?.title ?? `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.errorCode = problem?.errorCode ?? fallbackCode;
    this.traceId = problem?.traceId ?? undefined;
    this.fieldErrors = problem?.errors ?? {};
  }

  /** 401: the session is over (sign in again). */
  get isUnauthenticated(): boolean {
    return this.status === 401;
  }

  /** 400 with field errors: show them next to the fields. */
  get isValidation(): boolean {
    return this.status === 400 && Object.keys(this.fieldErrors).length > 0;
  }

  /** Worth retrying automatically: network failures, 408, 429 and 5xx (not 501). */
  get isTransient(): boolean {
    return (
      this.status === 0 ||
      this.status === 408 ||
      this.status === 429 ||
      (this.status >= 500 && this.status !== 501)
    );
  }

  /** Translation key of the message the UI shows. */
  get messageKey(): string {
    return this.errorCode.startsWith("AUX-") ? `errors.${this.errorCode}` : "errors.generic";
  }
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError;
}

/** Builds the ApiError of a non-2xx response from its body (ProblemDetails or anything else). */
export function apiErrorFrom(response: Response, body: unknown): ApiError {
  return new ApiError(response.status, isProblem(body) ? body : undefined);
}

/** A request that never got a response (offline, DNS, aborted by the network). */
export function networkError(cause: unknown): ApiError {
  const error = new ApiError(0, undefined, NETWORK_ERROR_CODE);
  (error as { cause?: unknown }).cause = cause;
  return error;
}

function isProblem(body: unknown): body is Problem {
  return (
    typeof body === "object" &&
    body !== null &&
    ("errorCode" in body || "title" in body || "status" in body)
  );
}

/**
 * The data of an openapi-fetch result, or the ApiError of its failure: `const me = unwrap(await api.GET("/api/v1/me"))`.
 */
export function unwrap<TData>(result: {
  data?: TData;
  error?: unknown;
  response: Response;
}): TData {
  if (result.response.ok) {
    return result.data as TData;
  }

  throw apiErrorFrom(result.response, result.error);
}
