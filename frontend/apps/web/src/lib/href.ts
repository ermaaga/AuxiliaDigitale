/** Link inside a tenant (skill auxilia-frontend-feature: never hard-code `/acme/...`): `tenantHref("acme", "/cases")`. */
export function tenantHref(tenant: string, route = "/"): string {
  const path = route.startsWith("/") ? route : `/${route}`;
  return path === "/" ? `/${tenant}` : `/${tenant}${path}`;
}

/**
 * Where to go after sign-in: the `next` parameter when it is a page of the same tenant (never an external URL or
 * another tenant, to avoid open redirects), otherwise the dashboard.
 */
export function safeNextPath(tenant: string, next: string | null | undefined): string {
  const home = tenantHref(tenant);
  if (
    !next ||
    (next !== home && !next.startsWith(`${home}/`)) ||
    next.includes("//") ||
    next.includes("\\")
  ) {
    return home;
  }

  return next;
}
