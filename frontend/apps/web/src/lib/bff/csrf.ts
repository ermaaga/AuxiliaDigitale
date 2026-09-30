/** Header every mutating BFF call must carry (a cross-site form or image cannot set it). */
export const CSRF_HEADER = "x-requested-with";
export const CSRF_HEADER_VALUE = "auxilia";

const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

/**
 * CSRF defence of the BFF (skill auxilia-security): SameSite=Lax cookies, plus on unsafe methods the custom header and
 * an `Origin` equal to the app origin. Returns the reason of a refusal, or `null`.
 */
export function csrfRefusal(request: Request, allowedOrigin: string | undefined): string | null {
  if (SAFE_METHODS.has(request.method.toUpperCase())) {
    return null;
  }

  if (request.headers.get(CSRF_HEADER) !== CSRF_HEADER_VALUE) {
    return "missing-header";
  }

  const origin = request.headers.get("origin");
  const expected = allowedOrigin ?? new URL(request.url).origin;
  return origin === expected ? null : "origin-mismatch";
}
