"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import type { CustomFieldDefinition } from "@/components/data-table/table-model";
import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type ClientListItem = components["schemas"]["ClientListItemResponse"];
export type ClientDetail = components["schemas"]["ClientDetailResponse"];
export type ClientEmployee = components["schemas"]["ClientEmployeeResponse"];
export type ClientSpecialization = components["schemas"]["ClientSpecializationResponse"];
export type CreateClient = components["schemas"]["CreateClientRequest"];
export type CreateClientResult = components["schemas"]["CreateClientResponse"];
export type UpdateClient = components["schemas"]["UpdateClientRequest"];
export type Tag = components["schemas"]["TagResponse"];
export type ClientTag = components["schemas"]["ClientTagResponse"];
export type ClientConsents = components["schemas"]["ClientConsentsResponse"];
export type RecordConsent = components["schemas"]["RecordConsentRequest"];

/** Query of the client lists (F05): `view` `all` or `mine`, the legacy filters and sorts. */
export type ClientListParams = {
  view: "all" | "mine";
  page: number;
  pageSize: number;
  sort?: string;
  "filter[fullName]"?: string;
  "filter[lastName]"?: string;
  "filter[email]"?: string;
  "filter[userName]"?: string;
  "filter[phone]"?: string;
  "filter[status]"?: string;
  "filter[employeeUserId]"?: string;
  "filter[tagId]"?: string;
};

/** The custom field entity of clients (F20, `ClientRules.CustomFieldEntity`). */
export const CLIENT_ENTITY = "client";

/** The grid of the client lists (F21, `directory.clients`). */
export const CLIENTS_GRID = "directory.clients";

const api = () => createBffClient("tenant");

/** Everything about clients lives under `clientsKey(tenant)`: a change invalidates it as a whole. */
export const clientsKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "directory", "clients")
    : queryKey(tenant, "directory", "clients", { entity, ...params });

export function useClients(tenant: string, params: ClientListParams) {
  return useQuery({
    queryKey: clientsKey(tenant, "list", params),
    queryFn: async () => unwrap(await api().GET("/api/v1/clients", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useClient(tenant: string, id: string) {
  return useQuery({
    queryKey: clientsKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/clients/{id}", { params: { path: { id } } })),
    retry: false,
  });
}

export function useAssignableEmployees(tenant: string, enabled = true) {
  return useQuery({
    queryKey: clientsKey(tenant, "assignable-employees"),
    queryFn: async () => unwrap(await api().GET("/api/v1/clients/assignable-employees")),
    enabled,
    staleTime: 60_000,
  });
}

export function useClientSpecializations(tenant: string, enabled = true) {
  return useQuery({
    queryKey: clientsKey(tenant, "specializations"),
    queryFn: async () => unwrap(await api().GET("/api/v1/clients/specializations")),
    enabled,
    staleTime: 60_000,
  });
}

/** Custom field definitions of clients for the signed-in user (F20); none when the API answers with an error. */
export function useClientCustomFields(tenant: string) {
  const query = useQuery({
    queryKey: queryKey(tenant, "configuration", "custom-fields", { entity: CLIENT_ENTITY }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/me/custom-fields/{entityType}", {
          params: { path: { entityType: CLIENT_ENTITY } },
        }),
      ),
    retry: false,
    staleTime: 60_000,
  });
  const definitions: CustomFieldDefinition[] = (query.data ?? []).map((field, order) => ({
    key: field.key,
    label: field.label,
    type: field.type,
    options: field.options,
    isRequired: field.isRequired,
    groupName: field.groupName,
    badgeColor: field.badgeColor,
    visibleOnGrid: field.visibleOnGrid,
    order: Number(field.order ?? order),
  }));
  return { definitions, isPending: query.isPending };
}

/** A mutation of a client that refreshes every client query of the tenant once settled. */
export function useClientMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: clientsKey(tenant) }),
  });
}

export async function createClient(body: CreateClient) {
  return unwrap(await api().POST("/api/v1/clients", { body }));
}

export async function updateClient(id: string, body: UpdateClient) {
  return unwrap(await api().PUT("/api/v1/clients/{id}", { params: { path: { id } }, body }));
}

export async function deleteClient(id: string) {
  await unwrap(await api().DELETE("/api/v1/clients/{id}", { params: { path: { id } } }));
}

export async function setClientSignIn(id: string, canSignIn: boolean) {
  return unwrap(
    await api().PUT("/api/v1/clients/{id}/sign-in", {
      params: { path: { id } },
      body: { canSignIn },
    }),
  );
}

export async function assignClientEmployee(id: string, employeeUserId: string) {
  return unwrap(
    await api().PUT("/api/v1/clients/{id}/employee", {
      params: { path: { id } },
      body: { employeeUserId },
    }),
  );
}

export async function unassignClientEmployee(id: string) {
  return unwrap(await api().DELETE("/api/v1/clients/{id}/employee", { params: { path: { id } } }));
}

export async function setClientSpecializations(id: string, specializationIds: readonly string[]) {
  return unwrap(
    await api().PUT("/api/v1/clients/{id}/specializations", {
      params: { path: { id } },
      body: { specializationIds: [...specializationIds] },
    }),
  );
}

export async function sendClientInvitation(id: string) {
  return unwrap(await api().POST("/api/v1/clients/{id}/invitation", { params: { path: { id } } }));
}

export async function resetClientPassword(id: string, sendLink: boolean) {
  return unwrap(
    await api().POST("/api/v1/clients/{id}/password-reset", {
      params: { path: { id } },
      body: { sendLink },
    }),
  );
}

/** "Rossi Mario" style full name used in lists and headers. */
export function clientName(client: Pick<ClientListItem, "firstName" | "lastName">): string {
  return `${client.firstName} ${client.lastName}`.trim();
}

/** The tags of the tenant (N01), with the number of clients of each. */
export function useTags(tenant: string, enabled = true) {
  return useQuery({
    queryKey: clientsKey(tenant, "tags"),
    queryFn: async () => unwrap(await api().GET("/api/v1/tags")),
    staleTime: 60_000,
    enabled,
  });
}

export function useClientTags(tenant: string, id: string) {
  return useQuery({
    queryKey: clientsKey(tenant, "client-tags", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/clients/{id}/tags", { params: { path: { id } } })),
  });
}

export function useClientConsents(tenant: string, id: string) {
  return useQuery({
    queryKey: clientsKey(tenant, "consents", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/clients/{id}/consents", { params: { path: { id } } })),
  });
}

export async function createTag(name: string, color: string | null) {
  return unwrap(await api().POST("/api/v1/tags", { body: { name, color } }));
}

export async function updateTag(id: string, name: string, color: string | null) {
  await unwrap(
    await api().PUT("/api/v1/tags/{id}", { params: { path: { id } }, body: { name, color } }),
  );
}

export async function deleteTag(id: string) {
  await unwrap(await api().DELETE("/api/v1/tags/{id}", { params: { path: { id } } }));
}

export async function setClientTags(id: string, tagIds: readonly string[]) {
  return unwrap(
    await api().PUT("/api/v1/clients/{id}/tags", {
      params: { path: { id } },
      body: { tagIds: [...tagIds] },
    }),
  );
}

/** Adds or removes tags on the selected clients (N01: bulk from the clients table). */
export async function changeClientsTags(
  clientIds: readonly string[],
  add: readonly string[],
  remove: readonly string[],
) {
  return unwrap(
    await api().POST("/api/v1/clients/tags", {
      body: { clientIds: [...clientIds], add: [...add], remove: [...remove] },
    }),
  );
}

export async function recordConsent(id: string, body: RecordConsent) {
  return unwrap(
    await api().POST("/api/v1/clients/{id}/consents", { params: { path: { id } }, body }),
  );
}
