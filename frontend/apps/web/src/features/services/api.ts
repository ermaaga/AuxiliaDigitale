"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type Service = components["schemas"]["ServiceResponse"];
export type ServiceCategory = components["schemas"]["ServiceCategoryResponse"];
export type ServiceFolder = components["schemas"]["ServiceFolderResponse"];
export type CreateService = components["schemas"]["CreateServiceRequest"];
export type UpdateService = components["schemas"]["UpdateServiceRequest"];

/** The grid of the service list (F21, `cases.services`). */
export const SERVICES_GRID = "cases.services";

/** Query of the service list (F08). */
export type ServiceListParams = {
  page: number;
  pageSize: number;
  sort?: string;
  "filter[name]"?: string;
  "filter[categoryId]"?: string;
  "filter[specializationId]"?: string;
  "filter[active]"?: boolean;
};

const api = () => createBffClient("tenant");

/**
 * Everything about the catalog lives under `servicesKey(tenant)` (the same prefix the case pages use for the service
 * choice and the folders): a change invalidates it as a whole.
 */
export const servicesKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "cases", "services")
    : queryKey(tenant, "cases", "services", { entity, ...params });

export function useServices(tenant: string, params: ServiceListParams) {
  return useQuery({
    queryKey: servicesKey(tenant, "list", params),
    queryFn: async () => unwrap(await api().GET("/api/v1/services", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useService(tenant: string, id: string) {
  return useQuery({
    queryKey: servicesKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/services/{id}", { params: { path: { id } } })),
    retry: false,
  });
}

export function useServiceCategories(tenant: string) {
  return useQuery({
    queryKey: servicesKey(tenant, "categories"),
    queryFn: async () => unwrap(await api().GET("/api/v1/service-categories")),
    staleTime: 60_000,
  });
}

/** The folder template of a service (F33), in tree order (the same key as the case documents). */
export function useServiceFolders(tenant: string, serviceId: string) {
  return useQuery({
    queryKey: servicesKey(tenant, "folders", { serviceId }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/services/{id}/folders", { params: { path: { id: serviceId } } }),
      ),
  });
}

export type ChecklistItem = components["schemas"]["ServiceChecklistItemResponse"];
export type ChecklistItemInput = components["schemas"]["ServiceChecklistItemRequest"];

/** The document checklist of a service (B-26), in order. */
export function useServiceChecklist(tenant: string, serviceId: string) {
  return useQuery({
    queryKey: servicesKey(tenant, "checklist", { serviceId }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/services/{id}/checklist", { params: { path: { id: serviceId } } }),
      ),
  });
}

/** Replaces the whole checklist; items with an id keep the ticks of the cases. */
export async function saveChecklist(serviceId: string, items: ChecklistItemInput[]) {
  return unwrap(
    await api().PUT("/api/v1/services/{id}/checklist", {
      params: { path: { id: serviceId } },
      body: { items },
    }),
  );
}

/** The active Employee specializations a service can require (F12). */
export function useEmployeeSpecializations(tenant: string) {
  return useQuery({
    queryKey: queryKey(tenant, "directory", "employees", { entity: "specializations" }),
    queryFn: async () => unwrap(await api().GET("/api/v1/employees/specializations")),
    staleTime: 60_000,
  });
}

/** A mutation of the catalog that refreshes every catalog query (and the cases showing service names) once settled. */
export function useServiceMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: servicesKey(tenant) });
      await client.invalidateQueries({ queryKey: queryKey(tenant, "cases", "cases") });
    },
  });
}

export async function createService(body: CreateService) {
  return unwrap(await api().POST("/api/v1/services", { body }));
}

export async function updateService(id: string, body: UpdateService) {
  return unwrap(await api().PUT("/api/v1/services/{id}", { params: { path: { id } }, body }));
}

export async function deleteService(id: string) {
  await unwrap(await api().DELETE("/api/v1/services/{id}", { params: { path: { id } } }));
}

export async function createCategory(name: string, description: string | null) {
  return unwrap(await api().POST("/api/v1/service-categories", { body: { name, description } }));
}

export async function updateCategory(
  id: string,
  name: string,
  description: string | null,
  isActive: boolean,
) {
  return unwrap(
    await api().PUT("/api/v1/service-categories/{id}", {
      params: { path: { id } },
      body: { name, description, isActive },
    }),
  );
}

export async function deleteCategory(id: string) {
  await unwrap(await api().DELETE("/api/v1/service-categories/{id}", { params: { path: { id } } }));
}

export async function createFolder(serviceId: string, name: string, parentId: string | null) {
  return unwrap(
    await api().POST("/api/v1/services/{id}/folders", {
      params: { path: { id: serviceId } },
      body: { name, parentId },
    }),
  );
}

export async function renameFolder(serviceId: string, folderId: string, name: string) {
  return unwrap(
    await api().PUT("/api/v1/services/{id}/folders/{folderId}", {
      params: { path: { id: serviceId, folderId } },
      body: { name },
    }),
  );
}

export async function reorderFolders(
  serviceId: string,
  parentId: string | null,
  folderIds: string[],
) {
  return unwrap(
    await api().PUT("/api/v1/services/{id}/folders/order", {
      params: { path: { id: serviceId } },
      body: { parentId, folderIds },
    }),
  );
}

export async function deleteFolder(serviceId: string, folderId: string) {
  return unwrap(
    await api().DELETE("/api/v1/services/{id}/folders/{folderId}", {
      params: { path: { id: serviceId, folderId } },
    }),
  );
}

/** The siblings of a folder in their order, with the folder moved by `offset` places (reorder by keyboard, F33). */
export function movedOrder(
  folders: readonly ServiceFolder[],
  folder: ServiceFolder,
  offset: -1 | 1,
): string[] | undefined {
  const siblings = folders.filter((item) => (item.parentId ?? null) === (folder.parentId ?? null));
  const index = siblings.findIndex((item) => item.id === folder.id);
  const target = index + offset;
  if (index < 0 || target < 0 || target >= siblings.length) {
    return undefined;
  }

  const ids = siblings.map((item) => item.id);
  [ids[index], ids[target]] = [ids[target]!, ids[index]!];
  return ids;
}
