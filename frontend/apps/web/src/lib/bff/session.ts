import { randomBytes } from "node:crypto";

import type { BffArea } from "./config";

/** Cookie of each area: only an opaque random id, the tokens stay on the server. */
export const SESSION_COOKIE: Record<BffArea, string> = {
  tenant: "__Host-aux_sid",
  platform: "__Host-aux_psid",
};

export type IssuedToken = { token: string; expiresAt: number };

export type BffSession = {
  id: string;
  area: BffArea;
  /** Tenant of a tenant-app session; every call goes to this tenant (the browser cannot choose another one). */
  tenant?: string;
  accessToken: string;
  /** Epoch milliseconds. */
  accessTokenExpiresAt: number;
  refreshToken: string;
  createdAt: number;
  /**
   * "Stay signed in" (N04): epoch milliseconds until the API keeps the session open without activity; the cookie and the
   * stored session last as long. Absent for an ordinary session (browser-session cookie, `sessionMaxAgeSeconds`).
   */
  rememberedUntil?: number;
  /** Console only: tenant-scoped platform tokens (10 minutes) by tenant slug. */
  tenantTokens?: Record<string, IssuedToken>;
};

/** Token pair returned by `POST /auth/token` and `POST /platform/auth/token`. */
export type TokenResponse = {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  refreshToken: string;
  /** Seconds, only for a "stay signed in" session (N04). */
  sessionExpiresIn?: number | null;
};

export function newSessionId(): string {
  return randomBytes(32).toString("base64url");
}

export function sessionFromTokens(
  area: BffArea,
  tokens: TokenResponse,
  now: number,
  tenant?: string,
): BffSession {
  return {
    id: newSessionId(),
    area,
    tenant,
    accessToken: tokens.accessToken,
    accessTokenExpiresAt: now + tokens.expiresIn * 1000,
    refreshToken: tokens.refreshToken,
    createdAt: now,
    ...(tokens.sessionExpiresIn ? { rememberedUntil: now + tokens.sessionExpiresIn * 1000 } : {}),
  };
}

/** `__Host-` cookies: Secure, Path=/, no Domain; HttpOnly and SameSite=Lax (CSRF defence with the custom header). */
export function sessionCookieOptions(maxAgeSeconds: number | undefined) {
  return {
    httpOnly: true,
    secure: true,
    sameSite: "lax" as const,
    path: "/",
    maxAge: maxAgeSeconds,
  };
}

const SLUG = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;

export function isTenantSlug(value: unknown): value is string {
  return typeof value === "string" && SLUG.test(value);
}
