"use client";

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";

import { tenantKey } from "./tenant-api";

export type TenantLogLevel = components["schemas"]["TenantLogLevelResponse"];
export type TenantLogEntry = components["schemas"]["TenantLogEntryResponse"];
export type TenantLogPage = components["schemas"]["TenantLogPageResponse"];

/** Serilog levels from the lowest (the minimum level filter). */
export const LOG_LEVELS = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"] as const;

/** How long debug logging can be enabled from the console (the API accepts up to 24 hours, D-28). */
export const DEBUG_DURATIONS = [
  { key: "duration30m", minutes: 30 },
  { key: "duration1h", minutes: 60 },
  { key: "duration4h", minutes: 240 },
  { key: "duration24h", minutes: 1440 },
] as const;

export type LogFilters = {
  from?: string;
  to?: string;
  level?: string;
  code?: string;
  traceId?: string;
  userId?: string;
  text?: string;
};

export const LOG_FILTERS = ["from", "to", "level", "code", "traceId", "userId", "text"] as const;

export const LOG_PAGE_SIZE = 50;

/** Console endpoints (console token): the files and the level are not tenant technical data (D-21). */
const platform = () => createBffClient("platform");

/** Under `tenantKey(slug, "logs")`, so a level change refreshes the level card and the events. */
export const logsKey = (slug: string, entity: string, params?: LogFilters) =>
  [...tenantKey(slug, "logs"), entity, params ?? {}] as const;

/** Only the filters that are set (the API treats a missing parameter as "no filter"). */
export function logQuery(filters: LogFilters, cursor?: string | null) {
  const query: LogFilters & { cursor?: string; pageSize: number } = { pageSize: LOG_PAGE_SIZE };
  for (const name of LOG_FILTERS) {
    const value = filters[name]?.trim();
    if (value) {
      query[name] = value;
    }
  }

  if (cursor) {
    query.cursor = cursor;
  }

  return query;
}

/** The end of a debug period that starts now. */
export function debugUntil(now: Date, minutes: number): string {
  return new Date(now.getTime() + minutes * 60_000).toISOString();
}

/** Badge look of a level: errors stand out, debug detail recedes. */
export function levelVariant(level: string): "destructive" | "secondary" | "outline" | "default" {
  switch (level) {
    case "Error":
    case "Fatal":
      return "destructive";
    case "Warning":
      return "default";
    case "Verbose":
    case "Debug":
      return "outline";
    default:
      return "secondary";
  }
}

export function useTenantLogLevel(slug: string) {
  return useQuery({
    queryKey: logsKey(slug, "level"),
    queryFn: async () =>
      unwrap(
        await platform().GET("/api/v1/platform/tenants/{slug}/log-level", {
          params: { path: { slug } },
        }),
      ),
  });
}

/** Newest first; each "load older" continues from the cursor of the last page. */
export function useTenantLogs(slug: string, filters: LogFilters) {
  return useInfiniteQuery({
    queryKey: logsKey(slug, "events", filters),
    initialPageParam: null as string | null,
    queryFn: async ({ pageParam }) =>
      unwrap(
        await platform().GET("/api/v1/platform/tenants/{slug}/logs", {
          params: { path: { slug }, query: logQuery(filters, pageParam) },
        }),
      ),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  });
}

export function useLogLevelMutation<TInput>(
  slug: string,
  run: (input: TInput) => Promise<TenantLogLevel>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (level) => client.setQueryData(logsKey(slug, "level"), level),
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "logs") }),
  });
}

export async function enableDebug(slug: string, until: string): Promise<TenantLogLevel> {
  return unwrap(
    await platform().PUT("/api/v1/platform/tenants/{slug}/log-level", {
      params: { path: { slug } },
      body: { until },
    }),
  );
}

export async function disableDebug(slug: string): Promise<TenantLogLevel> {
  return unwrap(
    await platform().DELETE("/api/v1/platform/tenants/{slug}/log-level", {
      params: { path: { slug } },
    }),
  );
}
