"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

import { tenantKey } from "./tenant-api";

export type CustomFieldEntity = components["schemas"]["CustomFieldEntityResponse"];
export type CustomField = components["schemas"]["CustomFieldDefinitionResponse"];
export type CreateCustomField = components["schemas"]["CreateCustomFieldRequest"];
export type UpdateCustomField = components["schemas"]["UpdateCustomFieldRequest"];
export type Grid = components["schemas"]["GridResponse"];
export type GridRoleLayout = components["schemas"]["GridRoleLayoutResponse"];

export const CUSTOM_FIELD_TYPES = [
  "Text",
  "Number",
  "Date",
  "Boolean",
  "Select",
  "MultiSelect",
] as const;

/** Custom fields and grids are technical endpoints of the tenant: a tenant-scoped platform token (D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

/** Under `tenantKey(slug, "customization")`, invalidated as a whole after every change. */
export const customizationKey = (slug: string, entity: string, params?: Record<string, unknown>) =>
  queryKey("platform", "tenants", `${slug}:customization`, { entity, ...params });

export function useCustomFieldEntities(slug: string) {
  return useQuery({
    queryKey: customizationKey(slug, "entities"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/custom-fields/entities")),
    staleTime: Number.POSITIVE_INFINITY,
  });
}

export function useCustomFields(slug: string, entityType: string | undefined) {
  return useQuery({
    queryKey: customizationKey(slug, "custom-fields", { entityType }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/custom-fields", {
          params: { query: { "filter[entityType]": entityType } },
        }),
      ),
    enabled: entityType !== undefined,
  });
}

export function useGrids(slug: string) {
  return useQuery({
    queryKey: customizationKey(slug, "grids"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/grids")),
  });
}

export function useCustomizationMutation<TInput, TOutput>(
  slug: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "customization") }),
  });
}

export async function createCustomField(slug: string, body: CreateCustomField) {
  return unwrap(await tenantClient(slug).POST("/api/v1/custom-fields", { body }));
}

export async function updateCustomField(slug: string, id: string, body: UpdateCustomField) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/custom-fields/{id}", { params: { path: { id } }, body }),
  );
}

export async function deleteCustomField(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/custom-fields/{id}", { params: { path: { id } } }),
  );
}

export async function saveGridLayout(
  slug: string,
  key: string,
  role: string,
  columns: ReadonlyArray<{ key: string; visible: boolean }>,
) {
  return unwrap(
    await tenantClient(slug).PUT("/api/v1/grids/{key}/layouts/{role}", {
      params: { path: { key, role } },
      body: { columns: [...columns] },
    }),
  );
}

export async function resetGridLayout(slug: string, key: string, role: string) {
  return unwrap(
    await tenantClient(slug).DELETE("/api/v1/grids/{key}/layouts/{role}", {
      params: { path: { key, role } },
    }),
  );
}

/** Moves a column up (-1) or down (+1); the list itself when the move leaves it. */
export function moveColumn<T>(columns: readonly T[], index: number, step: -1 | 1): T[] {
  const target = index + step;
  if (target < 0 || target >= columns.length) {
    return [...columns];
  }

  const next = [...columns];
  [next[index], next[target]] = [next[target]!, next[index]!];
  return next;
}
