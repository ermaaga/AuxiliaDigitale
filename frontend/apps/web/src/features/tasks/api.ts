"use client";

import {
  keepPreviousData,
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type TaskItem = components["schemas"]["TaskResponse"];
export type SaveTask = components["schemas"]["SaveTaskRequest"];
export type TimelineEntry = components["schemas"]["TimelineEntryResponse"];

export const TASK_STATUSES = ["Open", "Done"] as const;
export const TASK_SCOPES = ["mine", "all"] as const;
export const ACTIVITY_KINDS = ["Note", "Call", "Meeting", "Email"] as const;

/** Entries per page of the client timeline. */
export const TIMELINE_PAGE = 20;

export type TaskListParams = {
  scope?: string;
  page: number;
  pageSize: number;
  sort?: string;
  "filter[status]"?: string;
  "filter[clientId]"?: string;
  "filter[caseId]"?: string;
  "filter[due]"?: string;
};

const api = () => createBffClient("tenant");

/** Tasks and timelines live under `tasksKey(tenant)`. */
export const tasksKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "engagement", "tasks")
    : queryKey(tenant, "engagement", "tasks", { entity, ...params });

export function useTasks(tenant: string, params: TaskListParams) {
  return useQuery({
    queryKey: tasksKey(tenant, "list", params),
    queryFn: async () => unwrap(await api().GET("/api/v1/tasks", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useTask(tenant: string, id: string | undefined) {
  return useQuery({
    queryKey: tasksKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/tasks/{id}", { params: { path: { id: id! } } })),
    enabled: id !== undefined,
    retry: false,
  });
}

export function useTaskAssignees(tenant: string, enabled = true) {
  return useQuery({
    queryKey: tasksKey(tenant, "assignees"),
    queryFn: async () => unwrap(await api().GET("/api/v1/tasks/assignees")),
    staleTime: 5 * 60_000,
    enabled,
  });
}

/** The timeline of a client, newest first, a page at a time (`before` = the last entry shown). */
export function useClientTimeline(tenant: string, clientId: string) {
  return useInfiniteQuery({
    queryKey: tasksKey(tenant, "timeline", { clientId }),
    initialPageParam: undefined as string | undefined,
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api().GET("/api/v1/clients/{id}/timeline", {
          params: { path: { id: clientId }, query: { before: pageParam, take: TIMELINE_PAGE } },
        }),
      ),
    getNextPageParam: (last) =>
      last.length < TIMELINE_PAGE ? undefined : last[last.length - 1]?.at,
  });
}

/** A change of tasks refreshes every task query, the timelines and the dashboard. */
export function useTaskMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: tasksKey(tenant) });
      await client.invalidateQueries({ queryKey: queryKey(tenant, "reporting", "dashboard") });
    },
  });
}

export async function createTask(body: SaveTask) {
  return unwrap(await api().POST("/api/v1/tasks", { body }));
}

export async function updateTask(id: string, body: SaveTask) {
  return unwrap(await api().PUT("/api/v1/tasks/{id}", { params: { path: { id } }, body }));
}

export async function completeTask(id: string) {
  return unwrap(await api().POST("/api/v1/tasks/{id}/complete", { params: { path: { id } } }));
}

export async function reopenTask(id: string) {
  return unwrap(await api().POST("/api/v1/tasks/{id}/reopen", { params: { path: { id } } }));
}

export async function deleteTask(id: string) {
  await unwrap(await api().DELETE("/api/v1/tasks/{id}", { params: { path: { id } } }));
}

export async function addActivity(clientId: string, kind: string, text: string) {
  return unwrap(
    await api().POST("/api/v1/clients/{id}/activities", {
      params: { path: { id: clientId } },
      body: { kind, text, occurredAt: null },
    }),
  );
}

export async function deleteActivity(id: string) {
  await unwrap(await api().DELETE("/api/v1/activities/{id}", { params: { path: { id } } }));
}

/** The body of a task from the form: empty texts and dates become null. */
export function taskBody(values: {
  title: string;
  notes: string;
  dueOn: string;
  assigneeUserId: string;
  clientId?: string;
  caseId?: string;
}): SaveTask {
  return {
    title: values.title.trim(),
    notes: values.notes.trim() === "" ? null : values.notes.trim(),
    dueOn: values.dueOn === "" ? null : values.dueOn,
    assigneeUserId: values.assigneeUserId,
    clientId: values.clientId || null,
    caseId: values.caseId || null,
  };
}
