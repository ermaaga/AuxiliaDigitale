"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

import { tenantKey } from "./tenant-api";

export type RolePermission = components["schemas"]["RolePermissionResponse"];
export type Specialization = components["schemas"]["SpecializationResponse"];
export type SpecializationMember = components["schemas"]["SpecializationMemberResponse"];
export type CreateSpecialization = components["schemas"]["CreateSpecializationRequest"];
export type UpdateSpecialization = components["schemas"]["UpdateSpecializationRequest"];

/** The roles a specialization can belong to (Q34: a fixed list). */
export const SPECIALIZATION_ROLES = ["Employee", "Client"] as const;

/** Role permissions and specializations are technical endpoints of the tenant: a tenant-scoped platform token (D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

/** Under `tenantKey(slug, "access")`, invalidated as a whole after every change. */
export const accessKey = (slug: string, entity: string, params?: Record<string, unknown>) =>
  queryKey("platform", "tenants", `${slug}:access`, { entity, ...params });

export function useAccessMutation<TInput, TOutput>(
  slug: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "access") }),
  });
}

export function useRolePermissions(slug: string) {
  return useQuery({
    queryKey: accessKey(slug, "role-permissions"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/role-permissions")),
  });
}

export async function setRolePermissions(
  slug: string,
  role: string,
  permissions: readonly string[],
) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/role-permissions/{role}", {
      params: { path: { role } },
      body: { permissions: [...permissions] },
    }),
  );
}

export async function resetRolePermissions(slug: string, role: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/role-permissions/{role}", {
      params: { path: { role } },
    }),
  );
}

/** The permissions the role holds after switching `code` on or off. */
export function toggledPermissions(
  permissions: readonly RolePermission[],
  role: string,
  code: string,
  granted: boolean,
): string[] {
  return permissions
    .filter((permission) => (permission.code === code ? granted : permission.roles.includes(role)))
    .map((permission) => permission.code);
}

/** The role holds something other than the default permissions of the modules. */
export function differsFromDefaults(permissions: readonly RolePermission[], role: string): boolean {
  return permissions.some(
    (permission) => permission.roles.includes(role) !== permission.defaultRoles.includes(role),
  );
}

/** Permissions grouped by module, in the order the API lists them. */
export function byModule(
  permissions: readonly RolePermission[],
): Array<[string, RolePermission[]]> {
  const groups = new Map<string, RolePermission[]>();
  for (const permission of permissions) {
    groups.set(permission.module, [...(groups.get(permission.module) ?? []), permission]);
  }

  return [...groups];
}

export function useSpecializations(slug: string, role?: string) {
  return useQuery({
    queryKey: accessKey(slug, "specializations", { role }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/specializations", {
          params: { query: { "filter[role]": role } },
        }),
      ),
  });
}

export function useSpecializationMembers(slug: string, id: string) {
  return useQuery({
    queryKey: accessKey(slug, "members", { id }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/specializations/{id}/members", {
          params: { path: { id } },
        }),
      ),
  });
}

export function useSpecializationCandidates(slug: string, id: string, search: string) {
  return useQuery({
    queryKey: accessKey(slug, "candidates", { id, search }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/specializations/{id}/candidates", {
          params: { path: { id }, query: { search: search || undefined } },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

export async function createSpecialization(slug: string, body: CreateSpecialization) {
  return unwrap(await tenantClient(slug).POST("/api/v1/specializations", { body }));
}

export async function updateSpecialization(slug: string, id: string, body: UpdateSpecialization) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/specializations/{id}", {
      params: { path: { id } },
      body,
    }),
  );
}

export async function deactivateSpecialization(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/specializations/{id}", { params: { path: { id } } }),
  );
}

export async function addSpecializationMembers(
  slug: string,
  id: string,
  userIds: readonly string[],
) {
  await unwrap(
    await tenantClient(slug).POST("/api/v1/specializations/{id}/members", {
      params: { path: { id } },
      body: { userIds: [...userIds] },
    }),
  );
}

export async function removeSpecializationMember(slug: string, id: string, userId: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/specializations/{id}/members/{userId}", {
      params: { path: { id, userId } },
    }),
  );
}
