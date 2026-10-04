"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { BellIcon, CheckCheckIcon, XIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Popover, PopoverContent, PopoverTrigger } from "@auxilia/ui/components/popover";

import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useRealtimeConnected, useRealtimeEvent } from "@/lib/realtime/realtime";

import {
  deleteNotification,
  markAllRead,
  markRead,
  notificationKeys,
  notificationValues,
  useNotificationMutation,
  useNotifications,
  useUnreadCount,
  type NotificationItem,
} from "../api";
import { NotificationText } from "./notification-text";

/** The latest notifications in the panel (legacy: 10). */
const PANEL_SIZE = 10;

/**
 * The bell of the topbar (F16, skill auxilia-ui-design): unread badge, the latest notifications, mark all as read,
 * delete one; a click marks it read and opens its page (Q12). New notifications arrive by the realtime hub (with a
 * toast); without it the badge polls.
 */
export function NotificationBell({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const connected = useRealtimeConnected();
  const [open, setOpen] = React.useState(false);
  const unread = useUnreadCount(tenant, !connected);
  const list = useNotifications(tenant, { page: 1, pageSize: PANEL_SIZE }, open);
  const read = useNotificationMutation(tenant, markRead);
  const readAll = useNotificationMutation(tenant, markAllRead);
  const remove = useNotificationMutation(tenant, deleteNotification);
  const count = Number(unread.data?.count ?? 0);

  useRealtimeEvent("NotificationReceived", (payload) => {
    const pushed = payload as { kind?: string; parameters?: unknown };
    if (pushed?.kind) {
      const keys = notificationKeys(pushed.kind);
      if (t.has(keys.title)) {
        notify.info(t(keys.title, notificationValues(pushed.parameters)));
      }
    }
  });

  const openNotification = async (notification: NotificationItem) => {
    if (!notification.isRead) {
      await read.mutateAsync(notification.id).catch(() => undefined);
    }

    if (notification.link) {
      setOpen(false);
      router.push(tenantHref(tenant, notification.link));
    }
  };

  const label = count > 0 ? t("app.notifications.unreadCount", { count }) : t("Notifications");
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={label} className="relative">
          <BellIcon aria-hidden />
          {count > 0 ? (
            <span
              className="absolute -top-0.5 -right-0.5 min-w-5 rounded-full bg-destructive px-1 text-center text-xs leading-5 text-destructive-foreground"
              aria-hidden
            >
              {count > 99 ? "99+" : count}
            </span>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-0">
        <div className="flex items-center justify-between gap-2 border-b px-3 py-2">
          <p className="text-sm font-medium">{t("Notifications")}</p>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={count === 0 || readAll.isPending}
            onClick={() => void readAll.mutateAsync(undefined).catch(notify.error)}
          >
            <CheckCheckIcon aria-hidden /> {t("app.notifications.markAllRead")}
          </Button>
        </div>
        {(list.data?.items ?? []).length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted-foreground">
            {t("app.shell.notificationsEmpty")}
          </p>
        ) : (
          <ul className="flex max-h-96 flex-col overflow-y-auto" aria-label={t("Notifications")}>
            {(list.data?.items ?? []).map((notification) => (
              <li
                key={notification.id}
                className="flex items-start gap-1 border-b px-2 py-2 last:border-b-0"
              >
                <button
                  type="button"
                  className="min-w-0 flex-1 rounded-md p-1 hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring"
                  onClick={() => void openNotification(notification)}
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
                  <XIcon aria-hidden />
                </Button>
              </li>
            ))}
          </ul>
        )}
        <div className="border-t px-3 py-2">
          <Link
            href={tenantHref(tenant, "/notifications")}
            className="text-sm underline-offset-4 hover:underline"
            onClick={() => setOpen(false)}
          >
            {t("app.notifications.all")}
          </Link>
        </div>
      </PopoverContent>
    </Popover>
  );
}
