"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { CheckCheckIcon, Trash2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Switch } from "@auxilia/ui/components/switch";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  deleteNotification,
  markAllRead,
  markRead,
  notificationKeys,
  savePreferences,
  useNotificationMutation,
  useNotificationPreferences,
  useNotifications,
  type NotificationItem,
  type NotificationPreference,
} from "../api";
import { NotificationText } from "./notification-text";

const PAGE_SIZE = 20;

/**
 * `/{tenant}/notifications` (F16): every notification of the user (newest first, paged, unread only on request),
 * mark one or all as read, delete; and the preferences per kind (in the app, by e-mail).
 */
export function NotificationsPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const [page, setPage] = React.useState(1);
  const [unreadOnly, setUnreadOnly] = React.useState(false);
  const list = useNotifications(tenant, { page, pageSize: PAGE_SIZE, unreadOnly });
  const read = useNotificationMutation(tenant, markRead);
  const readAll = useNotificationMutation(tenant, markAllRead);
  const remove = useNotificationMutation(tenant, deleteNotification);
  const total = Number(list.data?.totalCount ?? 0);
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const open = async (notification: NotificationItem) => {
    if (!notification.isRead) {
      await read.mutateAsync(notification.id).catch(() => undefined);
    }

    if (notification.link) {
      router.push(tenantHref(tenant, notification.link));
    }
  };

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3">
          <CardTitle>{t("Notifications")}</CardTitle>
          <div className="flex flex-wrap items-center gap-3">
            <span className="flex items-center gap-2">
              <Switch
                id="notifications-unread"
                checked={unreadOnly}
                onCheckedChange={(value) => {
                  setUnreadOnly(value);
                  setPage(1);
                }}
              />
              <Label htmlFor="notifications-unread">{t("app.notifications.unreadOnly")}</Label>
            </span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={readAll.isPending}
              onClick={() =>
                void readAll
                  .mutateAsync(undefined)
                  .then(() => notify.success("app.notifications.allRead"), notify.error)
              }
            >
              <CheckCheckIcon aria-hidden /> {t("app.notifications.markAllRead")}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {list.error ? (
            <ApiErrorAlert error={list.error} onRetry={() => void list.refetch()} />
          ) : null}
          {list.isPending ? (
            <Skeleton className="h-32 w-full" />
          ) : (list.data?.items ?? []).length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("app.shell.notificationsEmpty")}</p>
          ) : (
            <ul className="flex flex-col divide-y" aria-label={t("Notifications")}>
              {(list.data?.items ?? []).map((notification) => (
                <li key={notification.id} className="flex items-start gap-2 py-2">
                  <button
                    type="button"
                    className="min-w-0 flex-1 rounded-md p-1 hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring"
                    onClick={() => void open(notification)}
                  >
                    <NotificationText notification={notification} />
                  </button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={t("app.notifications.delete")}
                    onClick={() => void remove.mutateAsync(notification.id).catch(notify.error)}
                  >
                    <Trash2Icon aria-hidden />
                  </Button>
                </li>
              ))}
            </ul>
          )}
          {pages > 1 ? (
            <div className="flex items-center justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={page <= 1}
                onClick={() => setPage(page - 1)}
              >
                {t("common.table.previousPage")}
              </Button>
              <span className="text-sm text-muted-foreground">
                {page} / {pages}
              </span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={page >= pages}
                onClick={() => setPage(page + 1)}
              >
                {t("common.table.nextPage")}
              </Button>
            </div>
          ) : null}
        </CardContent>
      </Card>
      <Preferences tenant={tenant} />
    </div>
  );
}

function Preferences({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const preferences = useNotificationPreferences(tenant);
  const save = useNotificationMutation(tenant, (items: NotificationPreference[]) =>
    savePreferences(items),
  );

  const change = async (
    preference: NotificationPreference,
    field: "inApp" | "email",
    value: boolean,
  ) => {
    try {
      await save.mutateAsync([{ ...preference, [field]: value }]);
      notify.success("app.notifications.preferencesSaved");
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("app.notifications.preferences")}</CardTitle>
        <CardDescription>{t("app.notifications.preferencesDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        {preferences.error ? (
          <ApiErrorAlert error={preferences.error} onRetry={() => void preferences.refetch()} />
        ) : null}
        <table className="w-full text-sm">
          <caption className="sr-only">{t("app.notifications.preferences")}</caption>
          <thead>
            <tr className="border-b text-left">
              <th scope="col" className="py-2 font-medium">
                {t("Type")}
              </th>
              <th scope="col" className="w-24 py-2 text-center font-medium">
                {t("app.notifications.inApp")}
              </th>
              <th scope="col" className="w-24 py-2 text-center font-medium">
                {t("Email")}
              </th>
            </tr>
          </thead>
          <tbody>
            {(preferences.data ?? []).map((preference) => {
              const title = notificationKeys(preference.kind).title;
              const name = t.has(title) ? t(title, {}) : preference.kind;
              return (
                <tr key={preference.kind} className="border-b last:border-b-0">
                  <th scope="row" className="py-2 text-left font-normal">
                    {name}
                  </th>
                  <td className="py-2 text-center">
                    <Switch
                      aria-label={`${name}: ${t("app.notifications.inApp")}`}
                      checked={preference.inApp}
                      disabled={save.isPending}
                      onCheckedChange={(value) => void change(preference, "inApp", value)}
                    />
                  </td>
                  <td className="py-2 text-center">
                    <Switch
                      aria-label={`${name}: ${t("Email")}`}
                      checked={preference.email}
                      disabled={save.isPending}
                      onCheckedChange={(value) => void change(preference, "email", value)}
                    />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}
