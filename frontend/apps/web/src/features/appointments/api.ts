"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type AppointmentListItem = components["schemas"]["AppointmentListItemResponse"];
export type AppointmentDetail = components["schemas"]["AppointmentResponse"];
export type ScheduleAppointment = components["schemas"]["ScheduleAppointmentRequest"];
export type RequestAppointment = components["schemas"]["RequestAppointmentRequest"];
export type UpdateAppointment = components["schemas"]["UpdateAppointmentRequest"];
export type AppointmentConflict = components["schemas"]["AppointmentConflictResponse"];

/** The statuses of an appointment (F13, Q19). */
export const APPOINTMENT_STATUSES = [
  "Pending",
  "Approved",
  "Rejected",
  "Completed",
  "Cancelled",
] as const;
export type AppointmentStatus = (typeof APPOINTMENT_STATUSES)[number];

/** The grid of the appointment lists (F21). */
export const APPOINTMENTS_GRID = "scheduling.appointments";

/** Query of the appointment grid (F13). `from`/`to` are local days of the tenant, both included. */
export type AppointmentListParams = {
  page: number;
  pageSize: number;
  sort?: string;
  from?: string;
  to?: string;
  "filter[clientId]"?: string;
  "filter[employeeUserId]"?: string;
  "filter[status]"?: string;
};

/** A calendar range: local days, both included (at most 62). */
export type CalendarRange = { from: string; to: string; global: boolean; clientId?: string };

/** The status changes the detail offers, with the API action. */
export type AppointmentAction = "approve" | "reject" | "complete" | "cancel";

const api = () => createBffClient("tenant");

/** Everything about appointments lives under `appointmentsKey(tenant)`: a change invalidates it as a whole. */
export const appointmentsKey = (
  tenant: string,
  entity?: string,
  params?: Record<string, unknown>,
) =>
  entity === undefined
    ? queryKey(tenant, "scheduling", "appointments")
    : queryKey(tenant, "scheduling", "appointments", { entity, ...params });

export function useAppointments(tenant: string, params: AppointmentListParams) {
  return useQuery({
    queryKey: appointmentsKey(tenant, "list", params),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/appointments", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useAppointmentCalendar(tenant: string, range: CalendarRange | null) {
  return useQuery({
    queryKey: appointmentsKey(tenant, "calendar", range ?? {}),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/appointments/calendar", {
          params: {
            query: {
              from: range!.from,
              to: range!.to,
              global: range!.global,
              clientId: range!.clientId,
            },
          },
        }),
      ),
    enabled: range !== null,
    placeholderData: keepPreviousData,
  });
}

export function useAppointment(tenant: string, id: string | null) {
  return useQuery({
    queryKey: appointmentsKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/appointments/{id}", { params: { path: { id: id! } } })),
    enabled: id !== null,
    retry: false,
  });
}

/** Clients: the employees an appointment can be requested with (Q22), the one in charge marked. */
export function useAppointmentEmployees(tenant: string, enabled: boolean) {
  return useQuery({
    queryKey: appointmentsKey(tenant, "employees"),
    queryFn: async () => unwrap(await api().GET("/api/v1/appointments/employees")),
    enabled,
    staleTime: 60_000,
  });
}

/** A change of an appointment that refreshes every appointment query once settled. */
export function useAppointmentMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: appointmentsKey(tenant) });
    },
  });
}

export async function scheduleAppointment(body: ScheduleAppointment) {
  return unwrap(await api().POST("/api/v1/appointments", { body }));
}

export async function requestAppointment(body: RequestAppointment) {
  return unwrap(await api().POST("/api/v1/appointments/requests", { body }));
}

export async function updateAppointment(id: string, body: UpdateAppointment) {
  return unwrap(await api().PUT("/api/v1/appointments/{id}", { params: { path: { id } }, body }));
}

/** A move (drag & drop): the same appointment at another time, every other value as it is now. */
export async function moveAppointment(
  id: string,
  date: string,
  time: string,
  durationMinutes: number,
) {
  const current = unwrap(
    await api().GET("/api/v1/appointments/{id}", { params: { path: { id } } }),
  );
  return updateAppointment(id, {
    date,
    time,
    durationMinutes,
    notes: current.notes,
    showInGlobalCalendar: current.showInGlobalCalendar,
    customFields: current.customFields,
  });
}

export async function changeAppointment(
  id: string,
  action: AppointmentAction,
  note: string | null,
) {
  const options = { params: { path: { id } }, body: { note } };
  switch (action) {
    case "approve":
      return unwrap(await api().POST("/api/v1/appointments/{id}/approve", options));
    case "reject":
      return unwrap(await api().POST("/api/v1/appointments/{id}/reject", options));
    case "complete":
      return unwrap(await api().POST("/api/v1/appointments/{id}/complete", options));
    case "cancel":
      return unwrap(await api().POST("/api/v1/appointments/{id}/cancel", options));
  }
}

export async function deleteAppointment(id: string) {
  await unwrap(await api().DELETE("/api/v1/appointments/{id}", { params: { path: { id } } }));
}

/** Staff: the open appointments of the employee that overlap a slot (a warning before saving). */
export async function findConflicts(query: {
  employeeUserId: string;
  date: string;
  time: string;
  durationMinutes: number;
  excludeId?: string;
}) {
  return unwrap(await api().GET("/api/v1/appointments/conflicts", { params: { query } }));
}

/** Whether the appointment still takes the employee's time (Pending or Approved). */
export const isOpen = (status: string) => status === "Pending" || status === "Approved";
