"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";

import { tenantKey } from "./tenant-api";

export type Job = components["schemas"]["JobResponse"];
export type JobRun = components["schemas"]["JobRunResponse"];

/** Runs shown under the jobs (the API accepts up to 100). */
export const JOB_RUNS_SHOWN = 20;

/** While a run is in progress or was just requested, the page refreshes itself (the Worker runs the job). */
export const JOB_REFRESH_MS = 3000;

/** Jobs are technical endpoints of the tenant: a tenant-scoped platform token (N02, D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

/** Under `tenantKey(slug, "jobs")`, invalidated as a whole after "run now". */
export const jobsKey = (slug: string, entity: "list" | "runs") =>
  [...tenantKey(slug, "jobs"), entity] as const;

/** The translation key of a job: `cases.expiry` → `casesExpiry` (a dotted code would nest in next-intl). */
export const jobKey = (code: string) =>
  code.replace(/\.(\w)/g, (_, letter: string) => letter.toUpperCase());

export function useJobs(slug: string, refreshing: boolean) {
  return useQuery({
    queryKey: jobsKey(slug, "list"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/jobs")),
    refetchInterval: (query) =>
      refreshing || query.state.data?.some((job) => job.isRunning) ? JOB_REFRESH_MS : false,
  });
}

export function useJobRuns(slug: string, refreshing: boolean) {
  return useQuery({
    queryKey: jobsKey(slug, "runs"),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/jobs/runs", {
          params: { query: { take: JOB_RUNS_SHOWN } },
        }),
      ),
    refetchInterval: (query) =>
      refreshing || query.state.data?.some((run) => run.status === "Running")
        ? JOB_REFRESH_MS
        : false,
  });
}

export function useRunJob(slug: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (code: string) =>
      unwrap(
        await tenantClient(slug).POST("/api/v1/jobs/{code}/run", {
          params: { path: { code } },
        }),
      ),
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "jobs") }),
  });
}
