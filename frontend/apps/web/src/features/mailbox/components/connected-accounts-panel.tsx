"use client";

import * as React from "react";
import { ApiError } from "@auxilia/api-client";
import {
  CalendarIcon,
  CircleAlertIcon,
  LinkIcon,
  MailIcon,
  MoreHorizontalIcon,
  PlusIcon,
} from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  changeAccountColor,
  disconnectAccount,
  isOAuthProvider,
  useConnectedAccountMutation,
  useConnectedAccountProviders,
  useConnectedAccounts,
  type ConnectedAccount,
  type ConnectedAccountProvider,
  type OAuthProvider,
} from "../api";
import { ACCOUNT_COLORS, accountColorStyle } from "../colors";
import { ImapConnectDialog } from "./imap-connect-dialog";
import { OAuthConnectDialog } from "./oauth-connect-dialog";
import { ProviderMark, providerLabel } from "./provider";

type OAuthChoice = ConnectedAccountProvider & { provider: OAuthProvider };

/** What the BFF callback reported (`?connected=<id>` or `?error=<code>`), kept by the page after the URL is cleaned. */
export type OAuthOutcome = { connected?: string; error?: string };

/**
 * "Connected accounts" of the profile (N05, C-03): "Sign in with Google", "Sign in with Microsoft" (only when this
 * installation has their OAuth application), "Connect PEC / IMAP"; the user's own accounts with colour, mail /
 * calendar, status, reconnect and disconnect. Nobody else sees them (the API answers with the caller's accounts only).
 */
export function ConnectedAccountsPanel({
  tenant,
  outcome,
  onOutcomeShown,
}: {
  tenant: string;
  outcome?: OAuthOutcome;
  onOutcomeShown?: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const accounts = useConnectedAccounts(tenant);
  const providers = useConnectedAccountProviders(tenant);
  const [oauth, setOAuth] = React.useState<{ provider: OAuthChoice; account?: ConnectedAccount }>();
  const [imap, setImap] = React.useState<{ account?: ConnectedAccount }>();

  // The outcome of the provider's consent (read once from the URL by the page): a toast for a connected account, an
  // alert for a failure; the page then cleans the URL.
  const connected = outcome?.connected;
  const shown = React.useRef(false);
  React.useEffect(() => {
    if (shown.current || (connected === undefined && outcome?.error === undefined)) {
      return;
    }

    shown.current = true;
    if (connected !== undefined) {
      notify.success("app.connectedAccounts.connected");
    }

    onOutcomeShown?.();
  }, [connected, outcome?.error, notify, onOutcomeShown]);

  const oauthProviders = (providers.data ?? []).filter((item): item is OAuthChoice =>
    isOAuthProvider(item.provider),
  );
  const offersImap = (providers.data ?? []).some((item) => item.provider === "imap");
  const choiceFor = (provider: string) => oauthProviders.find((item) => item.provider === provider);

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="flex items-center gap-2 text-base font-semibold">
            <LinkIcon aria-hidden className="size-4" />
            {t("app.connectedAccounts.title")}
          </h2>
        </CardTitle>
        <CardDescription>{t("app.connectedAccounts.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        {outcome?.error ? (
          <ApiErrorAlert error={new ApiError(400, { errorCode: outcome.error })} />
        ) : null}

        <section aria-labelledby="connect-account" className="flex flex-col gap-3">
          <h3 id="connect-account" className="text-sm font-medium">
            {t("app.connectedAccounts.connectNew")}
          </h3>
          {providers.error ? (
            <ApiErrorAlert error={providers.error} onRetry={() => void providers.refetch()} />
          ) : !providers.data ? (
            <Skeleton className="h-9 w-full sm:w-96" />
          ) : (
            <>
              <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                {oauthProviders.map((provider) => (
                  <Button
                    key={provider.provider}
                    type="button"
                    variant="outline"
                    onClick={() => setOAuth({ provider })}
                  >
                    <ProviderMark provider={provider.provider} />
                    {t(`app.connectedAccounts.signInWith.${provider.provider}`)}
                  </Button>
                ))}
                {offersImap ? (
                  <Button type="button" variant="outline" onClick={() => setImap({})}>
                    <PlusIcon aria-hidden />
                    {t("app.connectedAccounts.imap")}
                  </Button>
                ) : null}
              </div>
              {oauthProviders.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  {t("app.connectedAccounts.oauthNotConfigured")}
                </p>
              ) : null}
            </>
          )}
        </section>

        <section aria-labelledby="my-accounts" className="flex flex-col gap-3">
          <h3 id="my-accounts" className="text-sm font-medium">
            {t("app.connectedAccounts.mine")}
          </h3>
          {accounts.error ? (
            <ApiErrorAlert error={accounts.error} onRetry={() => void accounts.refetch()} />
          ) : !accounts.data ? (
            <div className="flex flex-col gap-2" aria-busy="true">
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-16 w-full" />
            </div>
          ) : accounts.data.length === 0 ? (
            <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
              {t("app.connectedAccounts.empty")}
            </p>
          ) : (
            <ul className="flex flex-col divide-y rounded-md border">
              {accounts.data.map((account) => (
                <AccountRow
                  key={account.id}
                  tenant={tenant}
                  account={account}
                  onReconnect={
                    isOAuthProvider(account.provider)
                      ? choiceFor(account.provider)
                        ? () => setOAuth({ provider: choiceFor(account.provider)!, account })
                        : undefined
                      : () => setImap({ account })
                  }
                />
              ))}
            </ul>
          )}
        </section>
      </CardContent>

      {oauth ? (
        <OAuthConnectDialog
          key={`${oauth.provider.provider}-${oauth.account?.id ?? "new"}`}
          provider={oauth.provider}
          account={oauth.account}
          open
          onOpenChange={(open) => (open ? undefined : setOAuth(undefined))}
        />
      ) : null}
      {imap ? (
        <ImapConnectDialog
          key={imap.account?.id ?? "new"}
          tenant={tenant}
          account={imap.account}
          open
          onOpenChange={(open) => (open ? undefined : setImap(undefined))}
        />
      ) : null}
    </Card>
  );
}

/** One account: colour, address, provider, mail / calendar, status and the actions menu. */
function AccountRow({
  tenant,
  account,
  onReconnect,
}: {
  tenant: string;
  account: ConnectedAccount;
  /** Undefined when the provider is no longer offered by this installation. */
  onReconnect?: () => void;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const recolor = useConnectedAccountMutation(tenant, (color: string) =>
    changeAccountColor(account.id, color),
  );
  const disconnect = useConnectedAccountMutation(tenant, () => disconnectAccount(account.id));
  const needsReconnect = account.status === "NeedsReconnect";
  const oauth = isOAuthProvider(account.provider);

  const changeColor = async (color: string) => {
    try {
      await recolor.mutateAsync(color);
      notify.success("app.connectedAccounts.colorSaved");
    } catch (error) {
      notify.error(error);
    }
  };

  const onDisconnect = async () => {
    const confirmed = await confirm({
      description: t("app.connectedAccounts.disconnectConfirm", { address: account.address }),
      confirmLabel: t("app.connectedAccounts.disconnect"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await disconnect.mutateAsync(undefined);
      notify.success("app.connectedAccounts.disconnected");
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <li
      className="flex flex-col gap-3 p-3 sm:flex-row sm:items-center"
      data-testid="connected-account"
    >
      <div className="flex min-w-0 flex-1 items-start gap-3">
        <span
          aria-hidden
          className="mt-1 size-3 shrink-0 rounded-full"
          style={accountColorStyle(account.color)}
        />
        <div className="flex min-w-0 flex-col gap-1">
          <p className="truncate font-medium" title={account.address}>
            {account.address}
          </p>
          <p className="text-sm text-muted-foreground">
            {providerLabel(t, account.provider)}
            {account.displayName ? ` · ${account.displayName}` : ""}
            {" · "}
            {t("app.connectedAccounts.colorName", {
              color: t(`app.connectedAccounts.colors.${account.color}`),
            })}
          </p>
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {needsReconnect ? (
              <Badge variant="destructive">
                <CircleAlertIcon aria-hidden />
                {t("app.connectedAccounts.statusNeedsReconnect")}
              </Badge>
            ) : (
              <Badge variant="secondary">{t("app.connectedAccounts.statusActive")}</Badge>
            )}
            {account.hasMail ? (
              <Badge variant="outline">
                <MailIcon aria-hidden />
                {t("app.connectedAccounts.mail")}
              </Badge>
            ) : null}
            {account.hasCalendar ? (
              <Badge variant="outline">
                <CalendarIcon aria-hidden />
                {t("app.connectedAccounts.calendar")}
              </Badge>
            ) : null}
            <span className="text-muted-foreground">
              {t("app.connectedAccounts.connectedOn", {
                date: format.dateTime(new Date(account.connectedAt), { dateStyle: "medium" }),
              })}
            </span>
          </div>
          {needsReconnect ? (
            <p className="text-sm text-destructive">
              {t("app.connectedAccounts.needsReconnectHint")}
              {account.lastErrorCode ? (
                <>
                  {" "}
                  <code className="rounded bg-muted px-1 text-xs text-foreground">
                    {account.lastErrorCode}
                  </code>
                </>
              ) : null}
            </p>
          ) : null}
        </div>
      </div>
      <div className="flex items-center gap-2 self-end sm:self-center">
        {needsReconnect && onReconnect ? (
          <Button type="button" size="sm" onClick={onReconnect}>
            {t("app.connectedAccounts.reconnect")}
          </Button>
        ) : null}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={t("app.connectedAccounts.actionsFor", { address: account.address })}
            >
              <MoreHorizontalIcon aria-hidden />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuSub>
              <DropdownMenuSubTrigger>{t("app.connectedAccounts.color")}</DropdownMenuSubTrigger>
              <DropdownMenuSubContent>
                <DropdownMenuRadioGroup
                  value={account.color}
                  onValueChange={(color) => void changeColor(color)}
                >
                  {ACCOUNT_COLORS.map((color) => (
                    <DropdownMenuRadioItem key={color} value={color}>
                      <span
                        aria-hidden
                        className="size-3 rounded-full"
                        style={accountColorStyle(color)}
                      />
                      {t(`app.connectedAccounts.colors.${color}`)}
                    </DropdownMenuRadioItem>
                  ))}
                </DropdownMenuRadioGroup>
              </DropdownMenuSubContent>
            </DropdownMenuSub>
            {onReconnect ? (
              <DropdownMenuItem onSelect={onReconnect}>
                {needsReconnect || !oauth
                  ? t("app.connectedAccounts.reconnect")
                  : t("app.connectedAccounts.changeAccess")}
              </DropdownMenuItem>
            ) : null}
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive" onSelect={() => void onDisconnect()}>
              {t("app.connectedAccounts.disconnect")}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </li>
  );
}
