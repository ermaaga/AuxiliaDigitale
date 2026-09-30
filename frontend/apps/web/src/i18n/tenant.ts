/** Request header set by `src/proxy.ts` with the tenant of the page (first path segment), for server code. */
export const TENANT_HEADER = "x-auxilia-tenant";

/** First path segments that are not tenants (ARCHITECTURE §14: `/{tenant}/…`, `/platform/…`). */
const RESERVED = new Set(["platform", "api", "design-system", "_next", "favicon.ico"]);

const SLUG = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;

/** `/acme/cases` → `acme`; `undefined` for the console and the app's own routes. */
export function tenantFromPath(pathname: string): string | undefined {
  const first = pathname.split("/")[1] ?? "";
  return SLUG.test(first) && !RESERVED.has(first) ? first : undefined;
}
