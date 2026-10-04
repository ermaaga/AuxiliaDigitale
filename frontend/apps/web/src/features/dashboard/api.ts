"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type Dashboard = components["schemas"]["DashboardResponse"];
export type DashboardCard = components["schemas"]["DashboardCardResponse"];
export type DashboardChart = components["schemas"]["DashboardChartResponse"];
export type DashboardList = components["schemas"]["DashboardListResponse"];

/** The periods of the charts (Q43). */
export const PERIODS = ["week", "month", "year", "all"] as const;
export type Period = (typeof PERIODS)[number];

export function useDashboard(tenant: string, period: Period) {
  return useQuery({
    queryKey: queryKey(tenant, "reporting", "dashboard", { period }),
    queryFn: async () =>
      unwrap(
        await createBffClient("tenant").GET("/api/v1/dashboard", { params: { query: { period } } }),
      ),
    placeholderData: keepPreviousData,
    // The figures move with every change in the other modules: refresh every minute while the page is open.
    refetchInterval: 60_000,
  });
}

/** The label of a chart point: a translation key (statuses) or the value itself, `YYYY-MM` / `YYYY-MM-DD` as dates. */
export function pointLabel(
  point: { label: string; labelKey?: string | null },
  translate: (key: string) => string,
  formatMonth: (date: Date) => string,
  formatDay: (date: Date) => string,
): string {
  if (point.labelKey) {
    return translate(point.labelKey);
  }

  if (/^\d{4}-\d{2}$/.test(point.label)) {
    return formatMonth(new Date(`${point.label}-01T00:00:00`));
  }

  if (/^\d{4}-\d{2}-\d{2}$/.test(point.label)) {
    return formatDay(new Date(`${point.label}T00:00:00`));
  }

  return point.label;
}
