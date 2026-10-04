"use client";

import Link from "next/link";
import { isApiError } from "@auxilia/api-client";
import {
  BriefcaseIcon,
  CircleDollarSignIcon,
  ListChecksIcon,
  MailIcon,
  MessageSquareIcon,
  PhoneIcon,
  Trash2Icon,
  UsersIcon,
  type LucideIcon,
} from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Textarea } from "@auxilia/ui/components/textarea";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  ACTIVITY_KINDS,
  addActivity,
  deleteActivity,
  useClientTimeline,
  useTaskMutation,
  type TimelineEntry,
} from "../api";
import { TASKS_PERMISSIONS } from "../permissions";
import { ACTIVITY_MAX, activitySchema, type ActivityValues } from "../schemas/task";

const ICONS: Record<string, LucideIcon> = {
  "activity:Note": MessageSquareIcon,
  "activity:Call": PhoneIcon,
  "activity:Meeting": UsersIcon,
  "activity:Email": MailIcon,
  "task.created": ListChecksIcon,
  "task.completed": ListChecksIcon,
  "case.status": BriefcaseIcon,
  "case.payment": CircleDollarSignIcon,
};

/** The icon of an entry: activities by their kind (the last part of the title key). */
export function entryIcon(entry: TimelineEntry): LucideIcon {
  const key =
    entry.kind === "activity" ? `activity:${entry.titleKey.split(".").at(-1)}` : entry.kind;
  return ICONS[key] ?? MessageSquareIcon;
}

/**
 * The timeline of a client (B-26, skill auxilia-ui-design "Client 360°"): staff write notes, calls, meetings and
 * e-mails; with them the tasks about the client and the cases the user sees (openings, status changes, payments),
 * newest first, a page at a time.
 */
export function ClientTimeline({ tenant, clientId }: { tenant: string; clientId: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canWrite = useCan(TASKS_PERMISSIONS.manageActivities);
  const timeline = useClientTimeline(tenant, clientId);
  const add = useTaskMutation(tenant, (values: ActivityValues) =>
    addActivity(clientId, values.kind, values.text),
  );
  const remove = useTaskMutation(tenant, (id: string) => deleteActivity(id));
  const form = useZodForm(activitySchema, { defaultValues: { kind: "Note", text: "" } });
  const entries = timeline.data?.pages.flat() ?? [];

  const submit = form.handleSubmit(async (values) => {
    try {
      await add.mutateAsync(values);
      notify.success("app.timeline.added");
      form.reset({ kind: values.kind, text: "" });
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, ["kind", "text"] as const);
      }
    }
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.timeline.title")}</h2>
        </CardTitle>
        <CardDescription>{t("app.timeline.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {canWrite ? (
          <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-3">
            {add.error &&
            !(isApiError(add.error) && Object.keys(add.error.fieldErrors).length > 0) ? (
              <ApiErrorAlert error={add.error} />
            ) : null}
            <div className="grid gap-3 sm:grid-cols-[12rem_1fr]">
              <FormField control={form.control} name="kind" label={t("app.timeline.kind")}>
                {(field, props) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger {...props}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {ACTIVITY_KINDS.map((kind) => (
                        <SelectItem key={kind} value={kind}>
                          {t(`app.timeline.kinds.${kind}`)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </FormField>
              <FormField control={form.control} name="text" label={t("app.timeline.text")}>
                {(field, props) => (
                  <Textarea {...props} {...field} rows={2} maxLength={ACTIVITY_MAX} />
                )}
              </FormField>
            </div>
            <Button type="submit" size="sm" className="self-end" disabled={add.isPending}>
              {t("app.timeline.add")}
            </Button>
          </form>
        ) : null}
        {timeline.error ? (
          <ApiErrorAlert error={timeline.error} onRetry={() => void timeline.refetch()} />
        ) : timeline.isPending ? (
          <div className="flex flex-col gap-2" aria-busy="true">
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
          </div>
        ) : entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.timeline.empty")}</p>
        ) : (
          <ol className="flex flex-col gap-3" aria-label={t("app.timeline.title")}>
            {entries.map((entry, index) => {
              const Icon = entryIcon(entry);
              const title = t(entry.titleKey, entry.parameters);
              return (
                <li
                  key={`${entry.kind}-${entry.at}-${entry.activityId ?? index}`}
                  className="flex gap-3"
                >
                  <span className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full bg-muted">
                    <Icon aria-hidden className="size-4" />
                  </span>
                  <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="font-medium">
                        {entry.link ? (
                          <Link
                            href={tenantHref(tenant, entry.link)}
                            className="underline-offset-4 hover:underline"
                          >
                            {title}
                          </Link>
                        ) : (
                          title
                        )}
                      </span>
                      <time dateTime={entry.at} className="text-xs text-muted-foreground">
                        {format.dateTime(new Date(entry.at), {
                          dateStyle: "medium",
                          timeStyle: "short",
                        })}
                      </time>
                    </div>
                    {entry.text ? (
                      <p className="whitespace-pre-wrap text-sm">{entry.text}</p>
                    ) : null}
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                      {entry.actorName ? <span>{entry.actorName}</span> : null}
                      {entry.canDelete && entry.activityId ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="h-6 px-2"
                          disabled={remove.isPending}
                          aria-label={t("app.timeline.deleteNamed", { title })}
                          onClick={async () => {
                            if (
                              await confirm({
                                description: t("app.timeline.deleteText"),
                                confirmLabel: t("Delete"),
                                variant: "destructive",
                              })
                            ) {
                              await remove.mutateAsync(entry.activityId!).then(
                                () => notify.success("app.timeline.deleted"),
                                (error: unknown) => notify.error(error),
                              );
                            }
                          }}
                        >
                          <Trash2Icon aria-hidden /> {t("Delete")}
                        </Button>
                      ) : null}
                    </div>
                  </div>
                </li>
              );
            })}
          </ol>
        )}
        {timeline.hasNextPage ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="self-center"
            disabled={timeline.isFetchingNextPage}
            onClick={() => void timeline.fetchNextPage()}
          >
            {t("app.timeline.more")}
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}
