import { callApi } from "./api";
import type { BffConfig } from "./config";
import type { BffSession, TokenResponse } from "./session";
import type { SessionStore } from "./session-store";

/** Access tokens are refreshed when they expire within this margin. */
const REFRESH_MARGIN_MS = 30_000;

const inFlight = new Map<string, Promise<BffSession | undefined>>();

/**
 * A session with a valid access token, refreshing it with the rotating refresh token when it is about to expire.
 * Single flight per session: concurrent requests share one refresh, because presenting the same refresh token twice
 * is treated by the API as reuse and ends the session. `undefined` when the API refused the refresh (session over).
 */
export async function withFreshAccessToken(
  config: BffConfig,
  store: SessionStore,
  session: BffSession,
  now: () => number = Date.now,
): Promise<BffSession | undefined> {
  if (session.accessTokenExpiresAt - REFRESH_MARGIN_MS > now()) {
    return session;
  }

  let pending = inFlight.get(session.id);
  if (pending === undefined) {
    pending = refresh(config, store, session, now).finally(() => inFlight.delete(session.id));
    inFlight.set(session.id, pending);
  }

  return pending;
}

async function refresh(
  config: BffConfig,
  store: SessionStore,
  session: BffSession,
  now: () => number,
): Promise<BffSession | undefined> {
  // Another request (or node sharing the store) may have refreshed already.
  const current = (await store.get(session.id)) ?? session;
  if (current.accessTokenExpiresAt - REFRESH_MARGIN_MS > now()) {
    return current;
  }

  const response = await callApi(config, current.area, {
    method: "POST",
    path: current.area === "platform" ? "platform/auth/token" : "auth/token",
    tenant: current.tenant,
    withClientSecret: true,
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ grantType: "refresh_token", refreshToken: current.refreshToken }),
  });

  if (!response.ok) {
    if (response.status === 400 || response.status === 401 || response.status === 403) {
      await store.delete(current.id);
      return undefined;
    }

    throw new Error(`Token refresh failed with status ${response.status}`);
  }

  const tokens = (await response.json()) as TokenResponse;
  const refreshed: BffSession = {
    ...current,
    accessToken: tokens.accessToken,
    accessTokenExpiresAt: now() + tokens.expiresIn * 1000,
    refreshToken: tokens.refreshToken,
    // A remembered session slides with the API's idle window.
    ...(tokens.sessionExpiresIn ? { rememberedUntil: now() + tokens.sessionExpiresIn * 1000 } : {}),
  };
  await store.set(refreshed, remainingSeconds(config, refreshed, now));
  return refreshed;
}

/** Seconds left of the session: the "stay signed in" window when remembered, otherwise the BFF's absolute lifetime. */
export function remainingSeconds(
  config: BffConfig,
  session: BffSession,
  now: () => number = Date.now,
): number {
  const end = session.rememberedUntil ?? session.createdAt + config.sessionMaxAgeSeconds * 1000;
  return Math.max(1, Math.floor((end - now()) / 1000));
}
