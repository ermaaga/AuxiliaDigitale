"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type TenantDetail = components["schemas"]["PlatformTenantDetailResponse"];
export type TenantModule = components["schemas"]["TenantModuleResponse"];
export type TenantAdministrator = components["schemas"]["TenantAdministratorResponse"];

/** Console queries are keyed under the console (`platform`), per tenant. */
export const tenantKey = (slug: string, entity?: string) =>
  queryKey("platform", "tenants", entity ? `${slug}:${entity}` : slug);

const platform = () => createBffClient("platform");
const path = (slug: string) => ({ params: { path: { slug } } });

/** A tenant, refreshed every 3 s while it is being provisioned (no realtime event for provisioning yet). */
export function useTenant(initial: TenantDetail) {
  return useQuery({
    queryKey: tenantKey(initial.slug),
    queryFn: async () =>
      unwrap(await platform().GET("/api/v1/platform/tenants/{slug}", path(initial.slug))),
    initialData: initial,
    refetchInterval: (query) => (query.state.data?.status === "Provisioning" ? 3_000 : false),
  });
}

export type TenantAction = "suspend" | "reactivate" | "archive" | "provisioning";

/**
 * Status actions and edits answer with the tenant as it is now: the cache takes it and the console (selector, list)
 * is rendered again.
 */
export function useTenantMutation<TInput>(
  slug: string,
  run: (input: TInput) => Promise<TenantDetail>,
) {
  const client = useQueryClient();
  const router = useRouter();
  return useMutation({
    mutationFn: run,
    onSuccess: (detail) => {
      client.setQueryData(tenantKey(slug), detail);
      void client.invalidateQueries({ queryKey: tenantKey(slug, "modules") });
      router.refresh();
    },
  });
}

export async function tenantAction(slug: string, action: TenantAction): Promise<TenantDetail> {
  const client = platform();
  switch (action) {
    case "suspend":
      return unwrap(await client.POST("/api/v1/platform/tenants/{slug}/suspend", path(slug)));
    case "reactivate":
      return unwrap(await client.POST("/api/v1/platform/tenants/{slug}/reactivate", path(slug)));
    case "archive":
      return unwrap(await client.POST("/api/v1/platform/tenants/{slug}/archive", path(slug)));
    case "provisioning":
      return unwrap(await client.POST("/api/v1/platform/tenants/{slug}/provisioning", path(slug)));
  }
}

export async function updateTenant(slug: string, body: { displayName: string; timeZone: string }) {
  return unwrap(await platform().PUT("/api/v1/platform/tenants/{slug}", { ...path(slug), body }));
}

export async function changePlan(slug: string, planCode: string) {
  return unwrap(
    await platform().PUT("/api/v1/platform/tenants/{slug}/plan", {
      ...path(slug),
      body: { planCode },
    }),
  );
}

export function usePlans() {
  return useQuery({
    queryKey: queryKey("platform", "plans"),
    queryFn: async () => unwrap(await platform().GET("/api/v1/platform/plans")),
  });
}

export function useTenantModules(slug: string) {
  return useQuery({
    queryKey: tenantKey(slug, "modules"),
    queryFn: async () =>
      unwrap(await platform().GET("/api/v1/platform/tenants/{slug}/modules", path(slug))),
  });
}

/** Sets (`isEnabled` + roles) or removes (`null`) the override of a module; answers with the modules. */
export async function saveModuleOverride(
  slug: string,
  moduleCode: string,
  override: { isEnabled: boolean; roles: string[] } | null,
) {
  const params = { params: { path: { slug, moduleCode } } };
  return override === null
    ? unwrap(
        await platform().DELETE("/api/v1/platform/tenants/{slug}/modules/{moduleCode}", params),
      )
    : unwrap(
        await platform().PUT("/api/v1/platform/tenants/{slug}/modules/{moduleCode}", {
          ...params,
          body: override,
        }),
      );
}

/** Technical endpoints of the tenant: the console BFF uses a tenant-scoped platform token (D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

export function useAdministrators(slug: string, enabled: boolean) {
  return useQuery({
    queryKey: tenantKey(slug, "administrators"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/administrators")),
    enabled,
  });
}

export async function createAdministrator(
  slug: string,
  body: { email: string; firstName: string; lastName: string },
) {
  return unwrap(await tenantClient(slug).POST("/api/v1/administrators", { body }));
}

export async function sendInvitation(slug: string, userId: string) {
  return unwrap(
    await tenantClient(slug).POST("/api/v1/administrators/{userId}/invitation", {
      params: { path: { userId } },
    }),
  );
}
