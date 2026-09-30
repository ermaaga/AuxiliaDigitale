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

/** Root of the platform console (ARCHITECTURE §14) and its home, the tenant list. */
export const PLATFORM_ROOT = "/platform";
export const PLATFORM_HOME = "/platform/tenants";

/** Link inside the console: `platformHref("/tenants/acme")` → `/platform/tenants/acme`. */
export function platformHref(route = "/"): string {
  const path = route.startsWith("/") ? route : `/${route}`;
  return path === "/" ? PLATFORM_ROOT : `${PLATFORM_ROOT}${path}`;
}

/** Page of a tenant in the console (`tenantConsoleHref("acme", "/settings")`). */
export function tenantConsoleHref(tenant: string, route = "/"): string {
  return platformHref(`/tenants/${encodeURIComponent(tenant)}${route === "/" ? "" : route}`);
}

/** The tenant selected in the console from the path (`/platform/tenants/acme/…` → `acme`). */
export function consoleTenantFromPath(pathname: string): string | undefined {
  const match = /^\/platform\/tenants\/([^/]+)/.exec(pathname);
  return match ? decodeURIComponent(match[1]!) : undefined;
}

/** The same console page for another tenant (`/platform/tenants/acme/jobs` → `/platform/tenants/beta/jobs`). */
export function switchConsoleTenant(pathname: string, tenant: string): string {
  const rest = /^\/platform\/tenants\/[^/]+(\/.*)?$/.exec(pathname)?.[1];
  return tenantConsoleHref(tenant, rest ?? "/");
}

/** Where to go after the console sign-in: `next` when it is a console page, otherwise the tenant list. */
export function safePlatformNextPath(next: string | null | undefined): string {
  if (
    !next ||
    !next.startsWith(`${PLATFORM_ROOT}/`) ||
    next.includes("//") ||
    next.includes("\\") ||
    next.startsWith(`${PLATFORM_ROOT}/login`) ||
    next.startsWith(`${PLATFORM_ROOT}/activate`)
  ) {
    return PLATFORM_HOME;
  }

  return next;
}
