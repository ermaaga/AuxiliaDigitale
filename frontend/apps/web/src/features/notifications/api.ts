"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type NotificationItem = components["schemas"]["NotificationResponse"];
export type NotificationPreference = components["schemas"]["NotificationPreferenceResponse"];

/** Polling of the badge when the realtime hub is not connected (legacy: every 30 s). */
export const FALLBACK_POLL_MS = 30_000;

const api = () => createBffClient("tenant");

/** Everything about notifications lives under `notificationsKey(tenant)` (also refreshed by `NotificationReceived`). */
export const notificationsKey = (
  tenant: string,
  entity?: string,
  params?: Record<string, unknown>,
) =>
  entity === undefined
    ? queryKey(tenant, "engagement", "notifications")
    : queryKey(tenant, "engagement", "notifications", { entity, ...params });

export function useUnreadCount(tenant: string, poll: boolean) {
  return useQuery({
    queryKey: notificationsKey(tenant, "unread"),
    queryFn: async () => unwrap(await api().GET("/api/v1/notifications/unread-count")),
    refetchInterval: poll ? FALLBACK_POLL_MS : false,
  });
}

export function useNotifications(
  tenant: string,
  params: { page: number; pageSize: number; unreadOnly?: boolean },
  enabled = true,
) {
  return useQuery({
    queryKey: notificationsKey(tenant, "list", params),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/notifications", { params: { query: params } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useNotificationPreferences(tenant: string) {
  return useQuery({
    queryKey: notificationsKey(tenant, "preferences"),
    queryFn: async () => unwrap(await api().GET("/api/v1/notifications/preferences")),
  });
}

export function useNotificationMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: notificationsKey(tenant) });
    },
  });
}

export async function markRead(id: string) {
  await unwrap(await api().POST("/api/v1/notifications/{id}/read", { params: { path: { id } } }));
}

export async function markAllRead() {
  await unwrap(await api().POST("/api/v1/notifications/read-all"));
}

export async function deleteNotification(id: string) {
  await unwrap(await api().DELETE("/api/v1/notifications/{id}", { params: { path: { id } } }));
}

export async function savePreferences(items: NotificationPreference[]) {
  return unwrap(await api().PUT("/api/v1/notifications/preferences", { body: { items } }));
}

/** The text placeholders of a notification as strings (the API stores a JSON object of strings). */
export function notificationValues(parameters: unknown): Record<string, string> {
  if (parameters === null || typeof parameters !== "object") {
    return {};
  }

  return Object.fromEntries(
    Object.entries(parameters as Record<string, unknown>).map(([key, value]) => [
      key,
      String(value ?? ""),
    ]),
  );
}

/** The translation keys of a kind (`notifications.{kind}.title|message`). */
export const notificationKeys = (kind: string) => ({
  title: `notifications.${kind}.title`,
  message: `notifications.${kind}.message`,
});
