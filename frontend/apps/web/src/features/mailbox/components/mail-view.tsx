"use client";

import * as React from "react";
import Link from "next/link";
import { ArrowLeftIcon, LinkIcon, PaperclipIcon, RefreshCwIcon, SearchIcon } from "lucide-react";
import { parseAsBoolean, parseAsInteger, parseAsString, useQueryState } from "nuqs";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Switch } from "@auxilia/ui/components/switch";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";
import { cn } from "@auxilia/ui/lib/utils";

import {
  useConnectedAccounts,
  useMailFolders,
  useMailMessages,
  type ConnectedAccount,
  type MailFolder,
  type MailMessage,
} from "../api";
import { accountName } from "../calendar";
import { accountColorStyle } from "../colors";
import { MAIL_PAGE_SIZE, mailAccounts, recipientsLabel, senderLabel } from "../mail";
import { SyncProgress, useAutoSync } from "./auto-sync";
import { MailReader } from "./mail-reader";

const options = { history: "replace" } as const;

/**
 * The mail page (N05 #100, C-07): the inbox and sent folders of each connected account (or all the mail), the stored
 * headers newest first with search and "unread only", and the message read live in the reading pane (`?open=`, the
 * deep link of the "new mail" notification works too). Opening the page asks for a mail sync once; the stored headers
 * show at once. On phones the reading pane replaces the list.
 */
export function MailView({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const [folderId, setFolderId] = useQueryState("folder", parseAsString.withOptions(options));
  const [search, setSearch] = useQueryState(
    "q",
    parseAsString.withDefault("").withOptions(options),
  );
  const [unread, setUnread] = useQueryState(
    "unread",
    parseAsBoolean.withDefault(false).withOptions(options),
  );
  const [page, setPage] = useQueryState("page", parseAsInteger.withDefault(1).withOptions(options));
  const [openId, setOpenId] = useQueryState("open", parseAsString.withOptions(options));
  const [draft, setDraft] = React.useState(search);
  const accounts = useConnectedAccounts(tenant);
  const folders = useMailFolders(tenant);
  const withMail = React.useMemo(
    () => mailAccounts(accounts.data, folders.data),
    [accounts.data, folders.data],
  );
  const syncable = withMail.some(({ account }) => account.status === "Active");
  const sync = useAutoSync(tenant, "mail", {
    enabled: accounts.isSuccess,
    syncable,
    completedKey: "app.mail.syncCompleted",
  });
  const folder = folders.data?.find((item) => item.id === folderId);
  const messages = useMailMessages(
    tenant,
    {
      folderId: folder?.id,
      search: search || undefined,
      "filter[unread]": unread || undefined,
      page,
      pageSize: MAIL_PAGE_SIZE,
    },
    accounts.isSuccess && withMail.length > 0,
  );
  const byAccount = React.useMemo(
    () => new Map(withMail.map(({ account }) => [account.id, account])),
    [withMail],
  );

  // The search runs a moment after typing stops.
  React.useEffect(() => {
    const timer = window.setTimeout(() => {
      if (draft.trim() !== search) {
        void setSearch(draft.trim() || null);
        void setPage(null);
      }
    }, 400);
    return () => window.clearTimeout(timer);
  }, [draft, search, setSearch, setPage]);

  if (accounts.error || folders.error) {
    return (
      <ApiErrorAlert
        error={accounts.error ?? folders.error}
        onRetry={() => void (accounts.error ? accounts.refetch() : folders.refetch())}
      />
    );
  }

  if (!accounts.isSuccess) {
    return <Skeleton className="h-96 w-full" />;
  }

  if (withMail.length === 0) {
    return (
      <div className="flex flex-col items-start gap-3 rounded-lg border p-6">
        <p className="text-muted-foreground">{t("app.mail.noAccounts")}</p>
        <Button asChild variant="outline">
          <Link href={tenantHref(tenant, "/profile?tab=accounts")}>
            <LinkIcon aria-hidden /> {t("app.mail.connect")}
          </Link>
        </Button>
      </div>
    );
  }

  const total = Number(messages.data?.totalCount ?? 0);
  const pages = Math.max(1, Math.ceil(total / MAIL_PAGE_SIZE));
  const choose = (id: string | null) => {
    void setFolderId(id);
    void setPage(null);
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SyncProgress active={sync.active} label={t("app.mail.syncing")} />
        {syncable ? (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={sync.syncing}
            onClick={sync.syncNow}
            className="ms-auto"
          >
            <RefreshCwIcon aria-hidden /> {t("app.externalCalendar.syncNow")}
          </Button>
        ) : null}
      </div>
      {sync.failed.length > 0 || withMail.some(({ account }) => account.status !== "Active") ? (
        <ul className="flex flex-col gap-1 text-sm text-destructive">
          {withMail
            .filter(({ account }) => account.status !== "Active")
            .map(({ account }) => (
              <li key={account.id}>
                {t("app.externalCalendar.reconnect", { name: accountName(account) })}{" "}
                <Link
                  className="underline underline-offset-4"
                  href={tenantHref(tenant, "/profile?tab=accounts")}
                >
                  {t("app.connectedAccounts.reconnect")}
                </Link>
              </li>
            ))}
          {sync.failed
            .filter((run) => byAccount.get(run.accountId)?.status === "Active")
            .map((run) => (
              <li key={run.runId}>
                {t("app.externalCalendar.failed", {
                  name: accountName(byAccount.get(run.accountId)!),
                  message: t.has(`errors.${run.errorCode}`)
                    ? t(`errors.${run.errorCode}`)
                    : t("errors.generic"),
                })}
              </li>
            ))}
        </ul>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[14rem_minmax(0,22rem)_minmax(0,1fr)]">
        <nav
          aria-label={t("app.mail.folders")}
          className={cn("flex flex-col gap-3", openId && "hidden lg:flex")}
        >
          <FolderButton
            current={!folder}
            onClick={() => choose(null)}
            label={t("app.mail.allMail")}
          />
          {withMail.map(({ account, folders: accountFolders }) => (
            <div key={account.id} className="flex flex-col gap-1">
              <p className="flex items-center gap-2 px-2 text-xs font-medium text-muted-foreground">
                <span
                  aria-hidden
                  className="size-2.5 shrink-0 rounded-full"
                  style={accountColorStyle(account.color)}
                />
                <span className="truncate">{accountName(account)}</span>
              </p>
              {accountFolders.length === 0 ? (
                <p className="px-2 text-xs text-muted-foreground">{t("app.mail.notSynced")}</p>
              ) : (
                accountFolders.map((item) => (
                  <FolderButton
                    key={item.id}
                    current={folder?.id === item.id}
                    onClick={() => choose(item.id)}
                    label={folderName(t, item)}
                    unread={item.unread}
                  />
                ))
              )}
            </div>
          ))}
        </nav>

        <section
          aria-label={t("app.mail.messages")}
          className={cn("flex min-w-0 flex-col gap-2", openId && "hidden lg:flex")}
        >
          <div className="flex flex-col gap-2">
            <div className="relative">
              <SearchIcon
                aria-hidden
                className="pointer-events-none absolute start-2.5 top-2.5 size-4 text-muted-foreground"
              />
              <Input
                type="search"
                className="ps-8"
                value={draft}
                onChange={(event) => setDraft(event.target.value)}
                placeholder={t("app.mail.search")}
                aria-label={t("app.mail.search")}
                maxLength={200}
              />
            </div>
            <span className="flex items-center gap-2">
              <Switch
                id="mail-unread"
                checked={unread}
                onCheckedChange={(value) => {
                  void setUnread(value || null);
                  void setPage(null);
                }}
              />
              <Label htmlFor="mail-unread">{t("app.mail.unreadOnly")}</Label>
            </span>
          </div>
          {messages.error ? (
            <ApiErrorAlert error={messages.error} onRetry={() => void messages.refetch()} />
          ) : null}
          {messages.isPending ? (
            <Skeleton className="h-80 w-full" />
          ) : messages.data && messages.data.items.length === 0 ? (
            <p className="rounded-md border p-4 text-sm text-muted-foreground">
              {search || unread ? t("app.mail.noResults") : t("app.mail.empty")}
            </p>
          ) : (
            <ul
              className="flex flex-col divide-y rounded-md border"
              aria-busy={messages.isFetching || undefined}
            >
              {(messages.data?.items ?? []).map((message) => (
                <li key={message.id}>
                  <MessageRow
                    message={message}
                    account={byAccount.get(message.accountId)}
                    sent={
                      folders.data?.find((item) => item.id === message.folderId)?.kind === "Sent"
                    }
                    current={message.id === openId}
                    onOpen={() => void setOpenId(message.id)}
                  />
                </li>
              ))}
            </ul>
          )}
          {pages > 1 ? (
            <div className="flex items-center justify-between gap-2 text-sm">
              <Button
                type="button"
                size="sm"
                variant="outline"
                disabled={page <= 1}
                onClick={() => void setPage(page - 1 > 1 ? page - 1 : null)}
              >
                {t("app.mail.previous")}
              </Button>
              <span className="text-muted-foreground">{t("app.mail.page", { page, pages })}</span>
              <Button
                type="button"
                size="sm"
                variant="outline"
                disabled={page >= pages}
                onClick={() => void setPage(page + 1)}
              >
                {t("app.mail.next")}
              </Button>
            </div>
          ) : null}
        </section>

        <section
          aria-label={t("app.mail.reader")}
          className={cn("min-w-0", !openId && "hidden lg:block")}
        >
          {openId ? (
            <div className="flex flex-col gap-3">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="self-start lg:hidden"
                onClick={() => void setOpenId(null)}
              >
                <ArrowLeftIcon aria-hidden /> {t("app.mail.back")}
              </Button>
              <MailReader
                tenant={tenant}
                id={openId}
                accounts={withMail.map(({ account }) => account)}
              />
            </div>
          ) : (
            <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground">
              {t("app.mail.choose")}
            </p>
          )}
        </section>
      </div>
    </div>
  );
}

function folderName(t: ReturnType<typeof useTranslations>, folder: MailFolder) {
  return folder.kind === "Sent" ? t("app.mail.sent") : t("app.mail.inbox");
}

function FolderButton({
  current,
  onClick,
  label,
  unread,
}: {
  current: boolean;
  onClick: () => void;
  label: string;
  unread?: number | string;
}) {
  const count = Number(unread ?? 0);
  return (
    <Button
      type="button"
      variant={current ? "secondary" : "ghost"}
      size="sm"
      className="justify-between"
      aria-current={current ? "true" : undefined}
      onClick={onClick}
    >
      <span className="truncate">{label}</span>
      {count > 0 ? <Badge variant="outline">{count}</Badge> : null}
    </Button>
  );
}

function MessageRow({
  message,
  account,
  sent,
  current,
  onOpen,
}: {
  message: MailMessage;
  account: ConnectedAccount | undefined;
  sent: boolean;
  current: boolean;
  onOpen: () => void;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const received = new Date(message.receivedAt);
  const today = new Date().toDateString() === received.toDateString();
  const who = sent ? recipientsLabel(message.to) : senderLabel(message);
  return (
    <button
      type="button"
      onClick={onOpen}
      aria-current={current ? "true" : undefined}
      className={cn(
        "flex w-full flex-col gap-0.5 px-3 py-2 text-start text-sm hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring",
        current && "bg-muted",
      )}
    >
      <span className="flex items-center gap-2">
        {account ? (
          <span
            aria-hidden
            className="size-2 shrink-0 rounded-full"
            style={accountColorStyle(account.color)}
          />
        ) : null}
        <span className={cn("min-w-0 flex-1 truncate", !message.isRead && "font-semibold")}>
          {sent ? `${t("app.mail.to")} ${who}` : who || t("app.mail.unknownSender")}
        </span>
        <time dateTime={message.receivedAt} className="shrink-0 text-xs text-muted-foreground">
          {format.dateTime(
            received,
            today ? { hour: "2-digit", minute: "2-digit" } : { day: "2-digit", month: "short" },
          )}
        </time>
      </span>
      <span className="flex items-center gap-2">
        <span
          className={cn(
            "min-w-0 flex-1 truncate",
            message.isRead ? "text-muted-foreground" : "font-medium",
          )}
        >
          {message.subject || t("app.mail.noSubject")}
        </span>
        {message.hasAttachments ? (
          <PaperclipIcon aria-label={t("app.mail.hasAttachments")} className="size-3.5 shrink-0" />
        ) : null}
        {!message.isRead ? <span className="sr-only">{t("app.mail.unread")}</span> : null}
      </span>
    </button>
  );
}
