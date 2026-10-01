"use client";

import * as React from "react";
import { PlusIcon, Trash2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { useNotify } from "@/lib/notify";

import { TENANT_ROLES, roleLabel } from "../../labels";
import {
  MESSAGE_PURPOSES,
  saveSenderRules,
  setAccountActive,
  setDefaultAccount,
  smtpSettingsOf,
  useMessagingAccounts,
  useMessagingMutation,
  useOutboundMessages,
  useSenderRules,
  type MessagingAccount,
  type OutboundMessage,
  type SenderRule,
} from "../../messaging-api";
import { rulesRequest, type RuleRow } from "../../schemas/messaging";
import { SmtpAccountDialog, TestMessageDialog } from "./account-dialogs";

/**
 * Outbound messaging of a tenant (S-03, N03, D-16): sending accounts (SMTP; WhatsApp modelled only, D-20), the rules
 * that pick the account by purpose and sender role, and the outbound log.
 */
export function TenantMessaging({ slug }: { slug: string }) {
  return (
    <div className="flex flex-col gap-4">
      <AccountsCard slug={slug} />
      <RulesCard slug={slug} />
      <OutboundCard slug={slug} />
    </div>
  );
}

type Dialog = { kind: "create" } | { kind: "edit" | "test"; account: MessagingAccount };

function AccountsCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const accounts = useMessagingAccounts(slug);
  const [dialog, setDialog] = React.useState<Dialog>();
  const change = useMessagingMutation(slug, (run: () => Promise<void>) => run());

  async function act(run: () => Promise<void>, done: string) {
    try {
      await change.mutateAsync(run);
      notify.success(done);
    } catch (error) {
      notify.error(error);
    }
  }

  const columns: DataTableColumn<MessagingAccount>[] = [
    {
      id: "name",
      header: t("app.platform.messaging.accountName"),
      hideable: false,
      mobile: "title",
      cell: (account) => (
        <span className="flex flex-wrap items-center gap-2 font-medium">
          {account.name}
          {account.isDefault ? <Badge>{t("Default")}</Badge> : null}
        </span>
      ),
    },
    {
      id: "channel",
      header: t("app.platform.messaging.channel"),
      cell: (account) => (
        <span className="flex flex-wrap items-center gap-2">
          {t(`app.platform.messaging.channelName.${account.channel}`)}
          {account.isAvailable ? null : (
            <Badge variant="outline">{t("app.platform.messaging.notAvailable")}</Badge>
          )}
        </span>
      ),
    },
    {
      id: "server",
      header: t("app.platform.messaging.server"),
      cell: (account) => {
        const settings = smtpSettingsOf(account);
        return account.provider === "smtp" ? (
          <span className="break-all">
            {settings.host}:{settings.port} · {settings.fromAddress}
          </span>
        ) : (
          account.provider
        );
      },
    },
    {
      id: "status",
      header: t("Status"),
      cell: (account) => (account.isActive ? t("Active") : t("Inactive")),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (account) => (
        <div className="flex flex-wrap gap-1">
          {account.provider === "smtp" ? (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setDialog({ kind: "edit", account })}
                aria-label={t("app.platform.messaging.editNamed", { account: account.name })}
              >
                {t("Edit")}
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!account.isActive}
                onClick={() => setDialog({ kind: "test", account })}
                aria-label={t("app.platform.messaging.testNamed", { account: account.name })}
              >
                {t("app.platform.messaging.sendTest")}
              </Button>
            </>
          ) : null}
          {account.isDefault || !account.isActive ? null : (
            <Button
              variant="ghost"
              size="sm"
              disabled={change.isPending}
              onClick={() =>
                void act(
                  () => setDefaultAccount(slug, account.id),
                  "app.platform.messaging.defaultSet",
                )
              }
              aria-label={t("app.platform.messaging.makeDefaultNamed", { account: account.name })}
            >
              {t("app.platform.messaging.makeDefault")}
            </Button>
          )}
          {account.isDefault ? null : (
            <Button
              variant="ghost"
              size="sm"
              disabled={change.isPending}
              onClick={async () => {
                if (
                  account.isActive &&
                  !(await confirm({
                    title: t("app.platform.messaging.deactivateTitle"),
                    description: t("app.platform.messaging.deactivateText", {
                      account: account.name,
                    }),
                    confirmLabel: t("app.platform.messaging.deactivate"),
                    variant: "destructive",
                  }))
                ) {
                  return;
                }

                await act(
                  () => setAccountActive(slug, account.id, !account.isActive),
                  "app.platform.messaging.accountSaved",
                );
              }}
              aria-label={t(
                account.isActive
                  ? "app.platform.messaging.deactivateNamed"
                  : "app.platform.messaging.activateNamed",
                { account: account.name },
              )}
            >
              {account.isActive
                ? t("app.platform.messaging.deactivate")
                : t("app.platform.messaging.activate")}
            </Button>
          )}
        </div>
      ),
    },
  ];

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-2">
        <div className="flex flex-col gap-1.5">
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.platform.messaging.accounts")}</h2>
          </CardTitle>
          <CardDescription>{t("app.platform.messaging.accountsDescription")}</CardDescription>
        </div>
        <Button size="sm" onClick={() => setDialog({ kind: "create" })}>
          <PlusIcon aria-hidden /> {t("app.platform.messaging.newAccount")}
        </Button>
      </CardHeader>
      <CardContent>
        <DataTable
          label={t("app.platform.messaging.accounts")}
          columns={columns}
          rows={accounts.data}
          getRowId={(account) => account.id}
          totalCount={accounts.data?.length ?? 0}
          page={1}
          pageSize={100}
          sort={null}
          onPageChange={() => {}}
          onPageSizeChange={() => {}}
          onSortChange={() => {}}
          isLoading={accounts.isPending}
          error={accounts.error}
          onRetry={() => void accounts.refetch()}
          hidePaging
        />
        {dialog?.kind === "create" ? (
          <SmtpAccountDialog slug={slug} onClose={() => setDialog(undefined)} />
        ) : null}
        {dialog?.kind === "edit" ? (
          <SmtpAccountDialog
            slug={slug}
            account={dialog.account}
            onClose={() => setDialog(undefined)}
          />
        ) : null}
        {dialog?.kind === "test" ? (
          <TestMessageDialog
            slug={slug}
            account={dialog.account}
            onClose={() => setDialog(undefined)}
          />
        ) : null}
      </CardContent>
    </Card>
  );
}

const rowsOf = (rules: readonly SenderRule[]): RuleRow[] =>
  rules.map((rule, index) => ({
    key: `${index}-${rule.accountId}`,
    purpose: rule.purpose,
    role: rule.role ?? "*",
    accountId: rule.accountId,
    priority: Number(rule.priority),
  }));

/** The e-mail rules: (purpose, role or any) → account; the default account answers whatever no rule covers. */
function RulesCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const accounts = useMessagingAccounts(slug);
  const rules = useSenderRules(slug, "Email");
  const usable = (accounts.data ?? []).filter(
    (account) => account.channel === "Email" && account.isActive,
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.messaging.rules")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.messaging.rulesDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        {rules.error ? (
          <ApiErrorAlert error={rules.error} onRetry={() => void rules.refetch()} />
        ) : rules.data && accounts.data ? (
          <RulesEditor
            key={JSON.stringify(rules.data)}
            slug={slug}
            initial={rowsOf(rules.data)}
            accounts={usable}
          />
        ) : (
          <Skeleton className="h-24 w-full" aria-busy />
        )}
      </CardContent>
    </Card>
  );
}

function RulesEditor({
  slug,
  initial,
  accounts,
}: {
  slug: string;
  initial: RuleRow[];
  accounts: readonly MessagingAccount[];
}) {
  const t = useTranslations();
  const notify = useNotify();
  const [rows, setRows] = React.useState(initial);
  const save = useMessagingMutation(slug, (next: RuleRow[]) =>
    saveSenderRules(slug, "Email", rulesRequest(next)),
  );
  const counter = React.useRef(initial.length);
  const update = (key: string, change: Partial<RuleRow>) =>
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...change } : row)));

  if (accounts.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        {t("app.platform.messaging.rulesNeedAccount")}
      </p>
    );
  }

  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        // Not `mutate(…, { onSuccess })`: the refreshed rules remount this editor and drop per-call callbacks.
        save.mutateAsync(rows).then(
          () => notify.success("app.platform.messaging.rulesSaved"),
          () => undefined,
        );
      }}
    >
      {save.error ? <ApiErrorAlert error={save.error} /> : null}
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("app.platform.messaging.noRules")}</p>
      ) : null}
      <ul className="flex flex-col gap-3">
        {rows.map((row, index) => {
          const label = (field: string) =>
            t("app.platform.messaging.ruleField", { field, number: index + 1 });
          return (
            <li
              key={row.key}
              className="grid gap-2 rounded-md border p-3 sm:grid-cols-[1fr_1fr_1.5fr_6rem_auto] sm:items-center"
            >
              <Select value={row.purpose} onValueChange={(purpose) => update(row.key, { purpose })}>
                <SelectTrigger
                  className="w-full"
                  aria-label={label(t("app.platform.messaging.purpose"))}
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {MESSAGE_PURPOSES.map((purpose) => (
                    <SelectItem key={purpose} value={purpose}>
                      {t(`app.platform.messaging.purposeName.${purpose}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Select value={row.role} onValueChange={(role) => update(row.key, { role })}>
                <SelectTrigger className="w-full" aria-label={label(t("Role"))}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="*">{t("app.platform.messaging.anyRole")}</SelectItem>
                  {TENANT_ROLES.map((role) => (
                    <SelectItem key={role} value={role}>
                      {roleLabel(t, role)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Select
                value={row.accountId}
                onValueChange={(accountId) => update(row.key, { accountId })}
              >
                <SelectTrigger
                  className="w-full"
                  aria-label={label(t("app.platform.messaging.account"))}
                >
                  <SelectValue placeholder={t("app.platform.messaging.account")} />
                </SelectTrigger>
                <SelectContent>
                  {accounts.map((account) => (
                    <SelectItem key={account.id} value={account.id}>
                      {account.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Input
                type="number"
                min={0}
                inputMode="numeric"
                value={row.priority}
                aria-label={label(t("app.platform.messaging.priority"))}
                onChange={(event) => update(row.key, { priority: Number(event.target.value) })}
              />
              <Button
                type="button"
                variant="ghost"
                size="icon"
                aria-label={t("app.platform.messaging.removeRule", { number: index + 1 })}
                onClick={() => setRows((current) => current.filter((item) => item.key !== row.key))}
              >
                <Trash2Icon aria-hidden />
              </Button>
            </li>
          );
        })}
      </ul>
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          onClick={() =>
            setRows((current) => [
              ...current,
              {
                key: `new-${counter.current++}`,
                purpose: "Notification",
                role: "*",
                accountId: accounts[0]!.id,
                priority: current.length + 1,
              },
            ])
          }
        >
          <PlusIcon aria-hidden /> {t("app.platform.messaging.addRule")}
        </Button>
        <Button type="submit" disabled={save.isPending}>
          {t("app.platform.messaging.saveRules")}
        </Button>
      </div>
    </form>
  );
}

const STATUSES = ["Queued", "Sent", "Failed"] as const;

/** The outbound log, newest first (paged by the API). */
function OutboundCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(10);
  const [status, setStatus] = React.useState<string>("all");
  const accounts = useMessagingAccounts(slug);
  const log = useOutboundMessages(slug, {
    page,
    pageSize,
    status: status === "all" ? undefined : status,
  });
  const names = new Map((accounts.data ?? []).map((account) => [account.id, account.name]));
  const format = (value: string | null | undefined) =>
    value
      ? new Date(value).toLocaleString(undefined, { dateStyle: "short", timeStyle: "short" })
      : "—";

  const columns: DataTableColumn<OutboundMessage>[] = [
    {
      id: "queuedAt",
      header: t("app.platform.messaging.queuedAt"),
      cell: (message) => format(message.queuedAt),
    },
    {
      id: "recipient",
      header: t("app.platform.messaging.recipient"),
      mobile: "title",
      hideable: false,
      cell: (message) => <span className="break-all">{message.recipient}</span>,
    },
    {
      id: "account",
      header: t("app.platform.messaging.account"),
      cell: (message) => names.get(message.accountId) ?? "—",
    },
    {
      id: "purpose",
      header: t("app.platform.messaging.purpose"),
      cell: (message) => t(`app.platform.messaging.purposeName.${message.purpose}`),
    },
    {
      id: "status",
      header: t("Status"),
      cell: (message) => (
        <span className="flex flex-wrap items-center gap-2">
          <Badge
            variant={
              message.status === "Failed"
                ? "destructive"
                : message.status === "Sent"
                  ? "default"
                  : "outline"
            }
          >
            {t(`app.platform.messaging.status.${message.status}`)}
          </Badge>
          {message.errorCode ? <code className="text-xs">{message.errorCode}</code> : null}
        </span>
      ),
    },
    {
      id: "completedAt",
      header: t("app.platform.messaging.completedAt"),
      cell: (message) => format(message.completedAt),
    },
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.messaging.log")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.messaging.logDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        <DataTable
          label={t("app.platform.messaging.log")}
          columns={columns}
          rows={log.data?.items}
          getRowId={(message) => message.id}
          totalCount={Number(log.data?.totalCount ?? 0)}
          page={page}
          pageSize={pageSize}
          sort={null}
          onPageChange={setPage}
          onPageSizeChange={(size) => {
            setPageSize(size);
            setPage(1);
          }}
          onSortChange={() => {}}
          isLoading={log.isPending}
          error={log.error}
          onRetry={() => void log.refetch()}
          toolbar={
            <div className="flex flex-col gap-1">
              <span id="log-status-label" className="text-sm font-medium">
                {t("Status")}
              </span>
              <Select
                value={status}
                onValueChange={(value) => {
                  setStatus(value);
                  setPage(1);
                }}
              >
                <SelectTrigger className="w-44" aria-labelledby="log-status-label">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{t("app.platform.messaging.allStatuses")}</SelectItem>
                  {STATUSES.map((value) => (
                    <SelectItem key={value} value={value}>
                      {t(`app.platform.messaging.status.${value}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          }
        />
      </CardContent>
    </Card>
  );
}
