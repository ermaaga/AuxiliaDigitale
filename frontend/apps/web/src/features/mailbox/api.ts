"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type ConnectedAccount = components["schemas"]["ConnectedAccountResponse"];
export type ConnectedAccountProvider = components["schemas"]["ConnectedAccountProviderResponse"];
export type ConnectImap = components["schemas"]["ConnectImapRequest"];
export type ImapServerSuggestion = components["schemas"]["ImapServerSuggestionResponse"];

/** Providers of "Sign in with …" (OAuth through the BFF, ADR 0021). */
export type OAuthProvider = "google" | "microsoft";

/** Permission of the "Connected accounts" tab (module `mailbox`, N05). */
export const CONNECT_ACCOUNTS_PERMISSION = "mailbox.accounts.connect";

const api = () => createBffClient("tenant");

export const mailboxKey = (tenant: string) => queryKey(tenant, "mailbox");
export const connectedAccountsKey = (tenant: string) => queryKey(tenant, "mailbox", "accounts");

export function useConnectedAccounts(tenant: string, enabled = true) {
  return useQuery({
    queryKey: connectedAccountsKey(tenant),
    queryFn: async () => unwrap(await api().GET("/api/v1/me/connected-accounts")),
    enabled,
  });
}

/** Google and Microsoft only when this deployment has their OAuth application; PEC / IMAP always. */
export function useConnectedAccountProviders(tenant: string) {
  return useQuery({
    queryKey: queryKey(tenant, "mailbox", "providers"),
    queryFn: async () => unwrap(await api().GET("/api/v1/me/connected-accounts/providers")),
    staleTime: 5 * 60_000,
  });
}

/** A change of an account: every mailbox query of the tenant is refreshed (the calendar and the mail use them). */
export function useConnectedAccountMutation<TInput, TResult>(
  tenant: string,
  run: (input: TInput) => Promise<TResult>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: () => client.invalidateQueries({ queryKey: mailboxKey(tenant) }),
  });
}

/** The provider's consent page for "Sign in with …"; `accountId` reconnects (or extends) an account. */
export async function startOAuth(
  provider: OAuthProvider,
  body: { mail: boolean; calendar: boolean; accountId?: string },
) {
  const started = unwrap(
    await api().POST("/api/v1/me/connected-accounts/oauth/{provider}/authorize", {
      params: { path: { provider } },
      body: { mail: body.mail, calendar: body.calendar, accountId: body.accountId ?? null },
    }),
  );
  return started.authorizationUrl;
}

export async function suggestImapServer(address: string) {
  return unwrap(
    await api().GET("/api/v1/me/connected-accounts/imap-server", {
      params: { query: { address } },
    }),
  );
}

export async function connectImap(body: ConnectImap) {
  return unwrap(await api().POST("/api/v1/me/connected-accounts/imap", { body }));
}

export async function changeAccountColor(id: string, color: string) {
  return unwrap(
    await api().PUT("/api/v1/me/connected-accounts/{id}", {
      params: { path: { id } },
      body: { color },
    }),
  );
}

export async function disconnectAccount(id: string) {
  await unwrap(
    await api().DELETE("/api/v1/me/connected-accounts/{id}", { params: { path: { id } } }),
  );
}

/** Whether a provider name of the API is one with OAuth. */
export function isOAuthProvider(provider: string): provider is OAuthProvider {
  return provider === "google" || provider === "microsoft";
}

export type AccountSync = components["schemas"]["AccountSyncResponse"];
export type ExternalCalendarEvent = components["schemas"]["CalendarEventResponse"];

/** Scope of a sync run (`SyncNames` of the API). */
export type SyncScope = "mail" | "calendar";

/** Permission of the sync and of the copied mail and events (module `mailbox`, N05). */
export const VIEW_MAIL_PERMISSION = "mailbox.mail.view";

/** Status poll while a run is active and the realtime hub is not connected (it pushes `AccountSyncCompleted`). */
const SYNC_POLL_MS = 5_000;

export const syncKey = (tenant: string, scope: SyncScope) =>
  queryKey(tenant, "mailbox", "sync", { scope });

/** A run still waiting for the Worker or in progress. */
export function isSyncActive(run: Pick<AccountSync, "status">) {
  return run.status === "Queued" || run.status === "Running";
}

/** The newest run of each account for the scope; polled while one is active and no realtime event will arrive. */
export function useSyncStatus(tenant: string, scope: SyncScope, enabled: boolean, poll: boolean) {
  return useQuery({
    queryKey: syncKey(tenant, scope),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/me/connected-accounts/sync", { params: { query: { scope } } }),
      ).runs,
    enabled,
    refetchInterval: (query) =>
      poll && (query.state.data ?? []).some(isSyncActive) ? SYNC_POLL_MS : false,
  });
}

/**
 * Starts a sync (N05 #106): the page opening (`manual: false`) or "Sync now" (`manual: true`). The answer holds the
 * run of each account (a recent or active one is returned instead of a new one) and becomes the status at once.
 */
export function useStartSync(tenant: string, scope: SyncScope) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (manual: boolean) =>
      unwrap(await api().POST("/api/v1/me/connected-accounts/sync", { body: { scope, manual } }))
        .runs,
    onSuccess: (runs) => client.setQueryData(syncKey(tenant, scope), runs),
  });
}

/** The copied events of my calendars in a range of local days (≤ 62, tenant time zone), read only. */
export function useExternalCalendarEvents(
  tenant: string,
  range: { from: string; to: string } | null,
  enabled: boolean,
) {
  return useQuery({
    queryKey: queryKey(tenant, "mailbox", "calendar-events", range ?? {}),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/me/connected-accounts/calendar-events", {
          params: { query: { from: range!.from, to: range!.to } },
        }),
      ),
    enabled: enabled && range !== null,
    placeholderData: keepPreviousData,
  });
}
