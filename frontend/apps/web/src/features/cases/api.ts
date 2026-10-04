"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type CaseListItem = components["schemas"]["CaseListItemResponse"];
export type CaseDetail = components["schemas"]["CaseResponse"];
export type OpenCase = components["schemas"]["OpenCaseRequest"];
export type CompleteCase = components["schemas"]["CompleteCaseRequest"];
export type AddCasePayment = components["schemas"]["AddCasePaymentRequest"];
export type ServiceFolder = components["schemas"]["ServiceFolderResponse"];
export type CaseService = components["schemas"]["ServiceResponse"];

/** The steps of a case (legacy `SubscriptionStatus`, F09), in order. */
export const CASE_STATUSES = ["Inserted", "InProgress", "Sent", "Completed"] as const;
export type CaseStatus = (typeof CASE_STATUSES)[number];

/** Query of the case lists (F09): legacy filters, sorts and the employee toggles. */
export type CaseListParams = {
  page: number;
  pageSize: number;
  sort?: string;
  showAll?: boolean;
  showCompleted?: boolean;
  "filter[clientName]"?: string;
  "filter[serviceName]"?: string;
  "filter[clientId]"?: string;
  "filter[serviceId]"?: string;
  "filter[status]"?: string;
};

const api = () => createBffClient("tenant");

/** Everything about cases lives under `casesKey(tenant)`: a change invalidates it as a whole. */
export const casesKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "cases", "cases")
    : queryKey(tenant, "cases", "cases", { entity, ...params });

export function useCases(tenant: string, params: CaseListParams) {
  return useQuery({
    queryKey: casesKey(tenant, "list", params),
    queryFn: async () => unwrap(await api().GET("/api/v1/cases", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useCase(tenant: string, id: string) {
  return useQuery({
    queryKey: casesKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/cases/{id}", { params: { path: { id } } })),
    retry: false,
  });
}

/** The active services a case can be opened for (F08), by name. */
export function useActiveServices(tenant: string, search: string, enabled = true) {
  return useQuery({
    queryKey: queryKey(tenant, "cases", "services", { entity: "choice", search }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/services", {
          params: {
            query: {
              "filter[active]": true,
              "filter[name]": search || undefined,
              pageSize: 50,
              sort: "name",
            },
          },
        }),
      ),
    enabled,
    staleTime: 30_000,
  });
}

/** The folder template of the service of a case (F33). */
export function useServiceFolders(tenant: string, serviceId: string) {
  return useQuery({
    queryKey: queryKey(tenant, "cases", "services", { entity: "folders", serviceId }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/services/{id}/folders", { params: { path: { id: serviceId } } }),
      ),
    staleTime: 60_000,
  });
}

/** A mutation of cases that refreshes every case query of the tenant (and the client status) once settled. */
export function useCaseMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: casesKey(tenant) });
      // The client's business status follows its cases (Q03).
      await client.invalidateQueries({ queryKey: queryKey(tenant, "directory", "clients") });
    },
  });
}

export async function openCase(body: OpenCase) {
  return unwrap(await api().POST("/api/v1/cases", { body }));
}

export async function advanceCase(id: string, note?: string) {
  return unwrap(
    await api().POST("/api/v1/cases/{id}/advance", {
      params: { path: { id } },
      body: { note: note ?? null },
    }),
  );
}

export async function moveCaseBack(id: string, note?: string) {
  return unwrap(
    await api().POST("/api/v1/cases/{id}/back", {
      params: { path: { id } },
      body: { note: note ?? null },
    }),
  );
}

export async function completeCase(id: string, body: CompleteCase) {
  return unwrap(
    await api().POST("/api/v1/cases/{id}/complete", { params: { path: { id } }, body }),
  );
}

export async function addCasePayment(id: string, body: AddCasePayment) {
  return unwrap(
    await api().POST("/api/v1/cases/{id}/payments", { params: { path: { id } }, body }),
  );
}

export async function deleteCase(id: string) {
  await unwrap(await api().DELETE("/api/v1/cases/{id}", { params: { path: { id } } }));
}

/** F11: e-mails the client the end date of the case (expiry, else due date). */
export async function sendExpiryReminder(id: string) {
  await unwrap(
    await api().POST("/api/v1/cases/{id}/expiry-reminder", { params: { path: { id } } }),
  );
}

/** Money with the currency of the case, in the page language. */
export function formatMoney(amount: number | string, currency: string, locale: string): string {
  return new Intl.NumberFormat(locale, { style: "currency", currency }).format(Number(amount));
}
