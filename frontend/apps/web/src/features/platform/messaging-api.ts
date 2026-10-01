"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

import { tenantKey } from "./tenant-api";

export type MessagingAccount = components["schemas"]["MessagingAccountResponse"];
export type SenderRule = components["schemas"]["SenderRuleResponse"];
export type OutboundMessage = components["schemas"]["OutboundMessageResponse"];
export type SmtpSettings = {
  host: string;
  port: number;
  security: "None" | "StartTls" | "SslOnConnect";
  username: string | null;
  fromAddress: string;
  fromName: string | null;
};

export const MESSAGE_PURPOSES = ["Transactional", "Notification", "Marketing"] as const;

/** Accounts, rules and log are technical endpoints of the tenant: a tenant-scoped platform token (D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

/** Under `tenantKey(slug, "messaging")`, so invalidating that prefix refreshes accounts, rules and log together. */
export const messagingKey = (slug: string, entity: string, params?: Record<string, unknown>) =>
  queryKey("platform", "tenants", `${slug}:messaging`, { entity, ...params });

export function useMessagingAccounts(slug: string) {
  return useQuery({
    queryKey: messagingKey(slug, "accounts"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/messaging/accounts")),
  });
}

export function useSenderRules(slug: string, channel: string) {
  return useQuery({
    queryKey: messagingKey(slug, "rules", { channel }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/messaging/rules", {
          params: { query: { "filter[channel]": channel } },
        }),
      ),
  });
}

export function useOutboundMessages(
  slug: string,
  query: { page: number; pageSize: number; status?: string; search?: string },
) {
  return useQuery({
    queryKey: messagingKey(slug, "outbound", query),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/messaging/outbound-messages", {
          params: {
            query: {
              page: query.page,
              pageSize: query.pageSize,
              "filter[status]": query.status,
              search: query.search || undefined,
            },
          },
        }),
      ),
  });
}

/** Any change to accounts or rules: accounts, rules and log are read again. */
export function useMessagingMutation<TInput, TOutput>(
  slug: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "messaging") }),
  });
}

export async function createSmtpAccount(
  slug: string,
  body: { name: string; settings: SmtpSettings; secret: string | null },
) {
  return unwrap(
    await tenantClient(slug).POST("/api/v1/messaging/accounts", {
      body: { channel: "Email", provider: "smtp", ...body, settings: body.settings as never },
    }),
  );
}

export async function updateAccount(
  slug: string,
  id: string,
  body: { name: string; settings: SmtpSettings; secret: string | null },
) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/messaging/accounts/{id}", {
      params: { path: { id } },
      body: { ...body, settings: body.settings as never },
    }),
  );
}

export async function setDefaultAccount(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).POST("/api/v1/messaging/accounts/{id}/default", {
      params: { path: { id } },
    }),
  );
}

export async function setAccountActive(slug: string, id: string, isActive: boolean) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/messaging/accounts/{id}/active", {
      params: { path: { id } },
      body: { isActive },
    }),
  );
}

export async function sendTestMessage(
  slug: string,
  id: string,
  body: { recipient: string; language: string },
) {
  return unwrap(
    await tenantClient(slug).POST("/api/v1/messaging/accounts/{id}/test", {
      params: { path: { id } },
      body,
    }),
  );
}

export async function saveSenderRules(
  slug: string,
  channel: string,
  rules: ReadonlyArray<{
    purpose: string;
    role: string | null;
    accountId: string;
    priority: number;
  }>,
) {
  return unwrap(
    await tenantClient(slug).PUT("/api/v1/messaging/rules/{channel}", {
      params: { path: { channel } },
      body: { rules: [...rules] },
    }),
  );
}

/** The SMTP settings of an account as the form edits them (missing fields get their defaults). */
export function smtpSettingsOf(account: MessagingAccount | undefined): SmtpSettings {
  const raw = (account?.settings ?? {}) as Partial<SmtpSettings>;
  return {
    host: raw.host ?? "",
    port: raw.port ?? 587,
    security: raw.security ?? "StartTls",
    username: raw.username ?? null,
    fromAddress: raw.fromAddress ?? "",
    fromName: raw.fromName ?? null,
  };
}
