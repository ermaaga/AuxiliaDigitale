/** Tenant statuses of the Catalog (`TenantStatus`), in lifecycle order. */
export const TENANT_STATUSES = [
  "Provisioning",
  "Active",
  "Suspended",
  "MigrationFailed",
  "Archived",
] as const;

/**
 * `Active` → its translation (`app.platform.tenantStatus.*`), unknown statuses as they are; with the slug,
 * `acme · Active`. Shared by server and client components.
 */
export function statusLabel(
  t: { (key: string): string; has(key: string): boolean },
  status: string,
  slug?: string,
): string {
  const key = `app.platform.tenantStatus.${status}`;
  const label = t.has(key) ? t(key) : status;
  return slug ? `${slug} · ${label}` : label;
}
