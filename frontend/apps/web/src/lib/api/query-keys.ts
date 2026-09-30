/**
 * Query keys `[tenant, module, entity, params?]` (skill auxilia-frontend-feature), e.g.
 * `queryKey("acme", "cases", "list", { page: 1 })`: invalidating `queryKey("acme", "cases")` refreshes every case query
 * of the tenant, and nothing leaks between tenants.
 */
export function queryKey(
  tenant: string,
  module: string,
  entity?: string,
  params?: Record<string, unknown>,
) {
  return [
    tenant,
    module,
    ...(entity === undefined ? [] : [entity]),
    ...(params === undefined ? [] : [params]),
  ] as const;
}

/** A fresh `Idempotency-Key` for one submission of a creating form (reuse it when the same submission is retried). */
export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}
