"use client";

import { useFormatter, useNow, useTranslations } from "next-intl";

import { notificationKeys, notificationValues, type NotificationItem } from "../api";

/** Title, message and time of a notification in the reader's language (unknown kinds show the kind). */
export function NotificationText({ notification }: { notification: NotificationItem }) {
  const t = useTranslations();
  const format = useFormatter();
  const now = useNow({ updateInterval: 60_000 });
  const keys = notificationKeys(notification.kind);
  const values = notificationValues(notification.parameters);
  return (
    <span className="flex min-w-0 flex-col gap-0.5 text-left">
      <span className="flex items-center gap-2 text-sm font-medium">
        {notification.isRead ? null : (
          <span className="size-2 shrink-0 rounded-full bg-primary" aria-hidden />
        )}
        {t.has(keys.title) ? t(keys.title, values) : notification.kind}
        {notification.isRead ? null : (
          <span className="sr-only">{t("app.notifications.unread")}</span>
        )}
      </span>
      {t.has(keys.message) ? (
        <span className="text-sm text-muted-foreground">{t(keys.message, values)}</span>
      ) : null}
      <span className="text-xs text-muted-foreground">
        {format.relativeTime(new Date(notification.createdAt), now)}
      </span>
    </span>
  );
}
