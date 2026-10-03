"use client";

import * as React from "react";
import Link from "next/link";
import { isApiError } from "@auxilia/api-client";
import { AlertTriangleIcon, CheckIcon, PencilIcon, Trash2Icon, XIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@auxilia/ui/components/sheet";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useHasRole } from "@/lib/permissions";

import {
  changeAppointment,
  deleteAppointment,
  useAppointment,
  useAppointmentMutation,
  type AppointmentAction,
  type AppointmentDetail,
} from "../api";
import { shortTime } from "../schemas/appointment";
import { AppointmentFormDialog } from "./appointment-form-dialog";
import { AppointmentStatusBadge } from "./appointment-status-badge";

const SUCCESS: Record<AppointmentAction, string> = {
  approve: "app.appointments.approved",
  reject: "app.appointments.rejected",
  complete: "app.appointments.completed",
  cancel: "app.appointments.cancelled",
};

/**
 * An appointment in a drawer (F13, legacy "manage appointment"): when, who, notes, conflict warning (staff) and the
 * timeline; the actions the API allows the caller: edit, approve / reject (pending), complete (approved, with
 * confirmation), cancel (with confirmation, the client too, Q21) and delete (staff, with confirmation).
 */
export function AppointmentSheet({
  tenant,
  id,
  onClose,
}: {
  tenant: string;
  id: string | null;
  onClose: () => void;
}) {
  const t = useTranslations();
  const query = useAppointment(tenant, id);

  return (
    <Sheet open={id !== null} onOpenChange={(open) => (open ? undefined : onClose())}>
      <SheetContent closeLabel={t("Close")} className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>{t("ManageAppointment")}</SheetTitle>
          <SheetDescription>{t("app.appointments.detailDescription")}</SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4 pb-4">
          {query.error ? (
            isApiError(query.error) && query.error.status === 404 ? (
              <Alert>
                <AlertDescription>{t("errors.AUX-15010")}</AlertDescription>
              </Alert>
            ) : (
              <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
            )
          ) : query.data ? (
            <Detail tenant={tenant} value={query.data} onClose={onClose} />
          ) : id !== null ? (
            <div className="flex flex-col gap-3" aria-busy="true">
              <Skeleton className="h-6 w-48" />
              <Skeleton className="h-24 w-full" />
            </div>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function Detail({
  tenant,
  value,
  onClose,
}: {
  tenant: string;
  value: AppointmentDetail;
  onClose: () => void;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const isAdministrator = useHasRole("Administrator");
  const isEmployee = useHasRole("Employee");
  const isStaff = isAdministrator || isEmployee;
  const [editing, setEditing] = React.useState(false);
  const change = useAppointmentMutation(
    tenant,
    (input: { action: AppointmentAction; note: string | null }) =>
      changeAppointment(value.id, input.action, input.note),
  );
  const remove = useAppointmentMutation(tenant, () => deleteAppointment(value.id));
  const busy = change.isPending || remove.isPending;

  const run = async (action: AppointmentAction) => {
    const needsConfirmation = action === "complete" || action === "cancel";
    if (
      needsConfirmation &&
      !(await confirm({
        description: t(
          action === "complete"
            ? "app.appointments.completeConfirm"
            : "app.appointments.cancelConfirm",
        ),
        confirmLabel: t(action === "complete" ? "Complete" : "app.appointments.cancel"),
        variant: action === "cancel" ? "destructive" : "default",
      }))
    ) {
      return;
    }

    try {
      await change.mutateAsync({ action, note: null });
      notify.success(SUCCESS[action]);
    } catch (error) {
      notify.error(error);
    }
  };

  const onDelete = async () => {
    if (
      await confirm({
        description: t("app.appointments.deleteConfirm"),
        confirmLabel: t("Delete"),
        variant: "destructive",
      })
    ) {
      try {
        await remove.mutateAsync(undefined);
        notify.success("app.appointments.deleted");
        onClose();
      } catch (error) {
        notify.error(error);
      }
    }
  };

  const when = format.dateTime(new Date(`${value.date}T${value.time}`), { dateStyle: "full" });
  return (
    <>
      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <AppointmentStatusBadge status={value.status} />
          {value.requestedByClient ? (
            <Badge variant="outline">{t("app.appointments.requestedByClient")}</Badge>
          ) : null}
        </div>
        <p className="text-lg font-semibold">
          {when}, {shortTime(value.time)} ·{" "}
          {t("app.appointments.minutes", { count: value.durationMinutes })}
        </p>
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
          <dt className="text-muted-foreground">{t("Client")}</dt>
          <dd>
            {isStaff ? (
              <Link
                href={tenantHref(tenant, `/clients/${value.client.id}`)}
                className="underline-offset-4 hover:underline"
              >
                {value.client.fullName}
              </Link>
            ) : (
              value.client.fullName
            )}
          </dd>
          <dt className="text-muted-foreground">{t("app.appointments.operator")}</dt>
          <dd>{value.employee.fullName}</dd>
          <dt className="text-muted-foreground">{t("ShowInGlobalCalendar")}</dt>
          <dd>{value.showInGlobalCalendar ? t("Yes") : t("No")}</dd>
        </dl>
        {value.notes ? (
          <p className="whitespace-pre-wrap rounded-md bg-muted p-3 text-sm">{value.notes}</p>
        ) : null}
        {value.hasConflict ? (
          <Alert>
            <AlertTriangleIcon aria-hidden />
            <AlertDescription>{t("app.appointments.hasConflict")}</AlertDescription>
          </Alert>
        ) : null}
      </div>

      <div className="flex flex-wrap gap-2">
        {value.canManage && value.status === "Pending" ? (
          <>
            <Button type="button" size="sm" disabled={busy} onClick={() => void run("approve")}>
              <CheckIcon aria-hidden /> {t("Approve")}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() => void run("reject")}
            >
              <XIcon aria-hidden /> {t("Reject")}
            </Button>
          </>
        ) : null}
        {value.canManage && value.status === "Approved" ? (
          <Button type="button" size="sm" disabled={busy} onClick={() => void run("complete")}>
            <CheckIcon aria-hidden /> {t("Complete")}
          </Button>
        ) : null}
        {value.canManage ? (
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={busy}
            onClick={() => setEditing(true)}
          >
            <PencilIcon aria-hidden /> {t("Edit")}
          </Button>
        ) : null}
        {value.canCancel ? (
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={busy}
            onClick={() => void run("cancel")}
          >
            {t("app.appointments.cancel")}
          </Button>
        ) : null}
        {value.canDelete ? (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={busy}
            onClick={() => void onDelete()}
          >
            <Trash2Icon aria-hidden /> {t("Delete")}
          </Button>
        ) : null}
      </div>

      <section className="flex flex-col gap-2">
        <h3 className="text-sm font-medium">{t("app.appointments.history")}</h3>
        <ol
          className="flex flex-col gap-2 border-l pl-4 text-sm"
          aria-label={t("app.appointments.history")}
        >
          {value.history.map((step, index) => (
            <li key={index} className="flex flex-col">
              <span className="font-medium">{t(step.toStatus as "Pending")}</span>
              <span className="text-muted-foreground">
                {format.dateTime(new Date(step.changedAt), {
                  dateStyle: "medium",
                  timeStyle: "short",
                })}
                {step.changedBy ? ` · ${step.changedBy.fullName}` : ""}
              </span>
              {step.note ? <span>{step.note}</span> : null}
            </li>
          ))}
        </ol>
      </section>

      {editing ? (
        <AppointmentFormDialog
          tenant={tenant}
          open={editing}
          onOpenChange={setEditing}
          appointment={value}
        />
      ) : null}
    </>
  );
}
