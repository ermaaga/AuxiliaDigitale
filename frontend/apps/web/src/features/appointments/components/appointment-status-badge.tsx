"use client";

import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";

const VARIANTS = {
  Pending: "outline",
  Approved: "default",
  Rejected: "destructive",
  Completed: "secondary",
  Cancelled: "secondary",
} as const;

/** The status of an appointment with its text (F13: never colour alone). */
export function AppointmentStatusBadge({ status }: { status: string }) {
  const t = useTranslations();
  const variant = VARIANTS[status as keyof typeof VARIANTS] ?? "outline";
  return <Badge variant={variant}>{t(status as keyof typeof VARIANTS)}</Badge>;
}
