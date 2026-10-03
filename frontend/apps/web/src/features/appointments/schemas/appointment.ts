import { z } from "zod";

/** Server limits (`Appointment` in the API, F13). */
export const MIN_DURATION = 5;
export const MAX_DURATION = 1440;
export const NOTES_MAX = 1000;
export const NOTE_MAX = 500;
export const DEFAULT_DURATION = 60;

const date = z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "validation.appointments.future");
const time = z.string().regex(/^\d{2}:\d{2}(:\d{2})?$/, "validation.appointments.time");
const duration = z
  .string()
  .trim()
  .refine(
    (value) =>
      /^\d+$/.test(value) && Number(value) >= MIN_DURATION && Number(value) <= MAX_DURATION,
    "validation.appointments.duration",
  );
const notes = z.string().max(NOTES_MAX, "validation.appointments.notes");

/** Staff schedule or edit an appointment (legacy employee modal); `clientId`/`employeeUserId` empty when fixed. */
export const appointmentSchema = z.object({
  clientId: z.string().min(1, "validation.appointments.client"),
  employeeUserId: z.string(),
  date,
  time,
  durationMinutes: duration,
  notes,
  showInGlobalCalendar: z.boolean(),
});

/** A client asks for an appointment (legacy client page): operator, preferred day and time, duration, notes. */
export const requestSchema = z.object({
  employeeUserId: z.string().min(1, "validation.appointments.employee"),
  date,
  time,
  durationMinutes: duration,
  notes,
});

export type AppointmentValues = z.input<typeof appointmentSchema>;
export type AppointmentOutput = z.output<typeof appointmentSchema>;
export type RequestValues = z.input<typeof requestSchema>;
export type RequestOutput = z.output<typeof requestSchema>;

/** `HH:mm` of an API time (`HH:mm:ss`). */
export const shortTime = (value: string) => value.slice(0, 5);

/** The API time of a form time (`HH:mm` → `HH:mm:00`). */
export const apiTime = (value: string) => (value.length === 5 ? `${value}:00` : value);

/** Wall-clock date and time now in a time zone (`YYYY-MM-DD`, `HH:mm`). */
export function nowIn(
  timeZone: string | undefined,
  now = new Date(),
): { date: string; time: string } {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(now);
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? "00";
  return {
    date: `${part("year")}-${part("month")}-${part("day")}`,
    time: `${part("hour")}:${part("minute")}`,
  };
}

/** `YYYY-MM-DD` plus `days`. */
export function addDays(day: string, days: number): string {
  const value = new Date(`${day}T00:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

/**
 * The calendar works on wall-clock times of the tenant written as UTC (FullCalendar `timeZone: "UTC"`): the start of an
 * appointment is its local date and time, the end adds the duration.
 */
export function wallClock(date: string, time: string, plusMinutes = 0): string {
  const value = new Date(`${date}T${apiTime(time)}Z`);
  value.setUTCMinutes(value.getUTCMinutes() + plusMinutes);
  return value.toISOString().slice(0, 19);
}

/** The local date and time of a calendar instant (UTC wall clock). */
export function fromWallClock(value: Date): { date: string; time: string } {
  const iso = value.toISOString();
  return { date: iso.slice(0, 10), time: iso.slice(11, 16) };
}
