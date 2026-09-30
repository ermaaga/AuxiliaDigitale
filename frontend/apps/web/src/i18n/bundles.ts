import { callApi } from "@/lib/bff/api";
import { readBffConfig } from "@/lib/bff/config";

import type { FlatMessages } from "./messages";

export type TenantLanguage = { code: string; name: string; isDefault: boolean };

type Cached<T> = { value: T; etag?: string; checkedAt: number };

/** Bundles are revalidated with the API (ETag, usually 304) at most this often per tenant and language. */
const BUNDLE_REVALIDATE_MS = 10_000;
const LANGUAGES_TTL_MS = 60_000;
/** A slow API must not block the page: the static fallback bundle is used instead. */
const TIMEOUT_MS = 3_000;

const bundles = new Map<string, Cached<FlatMessages>>();
const languages = new Map<string, Cached<TenantLanguage[]>>();

/**
 * The tenant's translations of a language from `GET /api/v1/i18n/{lang}` (anonymous, per tenant), cached in the
 * server process and revalidated with `If-None-Match`, so an edit in the console shows up within seconds. On failure
 * the last good bundle is kept; `undefined` when there never was one (the caller uses the static bundle).
 */
export async function tenantBundle(
  tenant: string,
  locale: string,
  now = Date.now(),
): Promise<FlatMessages | undefined> {
  const key = `${tenant}:${locale}`;
  const cached = bundles.get(key);
  if (cached && now - cached.checkedAt < BUNDLE_REVALIDATE_MS) {
    return cached.value;
  }

  try {
    const response = await get(tenant, `i18n/${encodeURIComponent(locale)}`, cached?.etag);
    if (response.status === 304 && cached) {
      bundles.set(key, { ...cached, checkedAt: now });
      return cached.value;
    }

    if (!response.ok) {
      return cached?.value;
    }

    const value = (await response.json()) as FlatMessages;
    bundles.set(key, { value, etag: response.headers.get("etag") ?? undefined, checkedAt: now });
    return value;
  } catch {
    return cached?.value;
  }
}

/** Active languages of the tenant (`GET /api/v1/i18n/languages`), cached for a minute; `undefined` if unreachable. */
export async function tenantLanguages(
  tenant: string,
  now = Date.now(),
): Promise<TenantLanguage[] | undefined> {
  const cached = languages.get(tenant);
  if (cached && now - cached.checkedAt < LANGUAGES_TTL_MS) {
    return cached.value;
  }

  try {
    const response = await get(tenant, "i18n/languages");
    if (!response.ok) {
      return cached?.value;
    }

    const value = (await response.json()) as TenantLanguage[];
    languages.set(tenant, { value, checkedAt: now });
    return value;
  } catch {
    return cached?.value;
  }
}

/** For tests. */
export function clearBundleCache() {
  bundles.clear();
  languages.clear();
}

function get(tenant: string, path: string, etag?: string) {
  return callApi(readBffConfig(), "tenant", {
    method: "GET",
    path,
    tenant,
    headers: etag ? { "if-none-match": etag } : undefined,
    signal: AbortSignal.timeout(TIMEOUT_MS),
  });
}
