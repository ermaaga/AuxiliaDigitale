"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type ActiveSession = components["schemas"]["ActiveSessionResponse"];

/** Legacy page: refreshed every few seconds. */
export const REFRESH_MS = 10_000;

export type SessionListParams = {
  page: number;
  pageSize: number;
  sort?: string;
  "filter[userName]"?: string;
};

const api = () => createBffClient("tenant");

export const sessionsKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "identity", "sessions")
    : queryKey(tenant, "identity", "sessions", { entity, ...params });

export function useActiveSessions(tenant: string, params: SessionListParams) {
  return useQuery({
    queryKey: sessionsKey(tenant, "list", params),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/identity/sessions", { params: { query: params } })),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
  });
}

export function useSessionSummary(tenant: string) {
  return useQuery({
    queryKey: sessionsKey(tenant, "summary"),
    queryFn: async () => unwrap(await api().GET("/api/v1/identity/sessions/summary")),
    refetchInterval: REFRESH_MS,
  });
}

export function useRevokeSession(tenant: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api().DELETE("/api/v1/identity/sessions/{id}", { params: { path: { id } } })),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sessionsKey(tenant) });
    },
  });
}

/** Minutes between two instants, at least 0 (the duration column). */
export function minutesBetween(from: string, to: Date): number {
  return Math.max(0, Math.round((to.getTime() - new Date(from).getTime()) / 60_000));
}
