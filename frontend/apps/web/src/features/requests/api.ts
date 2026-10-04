"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type RequestListItem = components["schemas"]["RequestListItemResponse"];
export type RequestDetail = components["schemas"]["RequestResponse"];

/** Request types (F15, Q18). */
export const REQUEST_TYPES = ["Information", "General", "Support", "Appointment"] as const;
export const REQUEST_STATUSES = ["Pending", "Responded", "Closed"] as const;
export const REQUEST_BOXES = ["received", "sent", "all"] as const;
export type RequestBox = (typeof REQUEST_BOXES)[number];

/** The grid of the inbox (F21). */
export const REQUESTS_GRID = "engagement.requests";

export type RequestListParams = {
  box: RequestBox;
  page: number;
  pageSize: number;
  sort?: string;
  "filter[status]"?: string;
  "filter[type]"?: string;
};

const api = () => createBffClient("tenant");

/** Everything about requests lives under `requestsKey(tenant)` (also refreshed by `RequestChanged`). */
export const requestsKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "engagement", "requests")
    : queryKey(tenant, "engagement", "requests", { entity, ...params });

export function useRequests(tenant: string, params: RequestListParams) {
  return useQuery({
    queryKey: requestsKey(tenant, "list", params),
    queryFn: async () => unwrap(await api().GET("/api/v1/requests", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useRequest(tenant: string, id: string) {
  return useQuery({
    queryKey: requestsKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/requests/{id}", { params: { path: { id } } })),
    retry: false,
  });
}

export function useRequestMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: requestsKey(tenant) });
    },
  });
}

export async function createRequest(body: components["schemas"]["CreateRequestRequest"]) {
  return unwrap(await api().POST("/api/v1/requests", { body }));
}

export async function replyToRequest(id: string, message: string) {
  return unwrap(
    await api().POST("/api/v1/requests/{id}/messages", {
      params: { path: { id } },
      body: { message },
    }),
  );
}

export async function closeRequest(id: string) {
  return unwrap(await api().POST("/api/v1/requests/{id}/close", { params: { path: { id } } }));
}

export async function deleteRequest(id: string) {
  await unwrap(await api().DELETE("/api/v1/requests/{id}", { params: { path: { id } } }));
}
