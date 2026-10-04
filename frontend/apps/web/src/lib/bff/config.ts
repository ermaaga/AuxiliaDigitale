/*
 * Server-only configuration of the two BFFs (skill auxilia-security): the API base URL and the client applications
 * the Next.js server authenticates as. Secrets come from the environment (never NEXT_PUBLIC_*, never in the browser).
 */

export type BffArea = "tenant" | "platform";

export type ClientCredentials = { id: string; secret: string | undefined };

export type BffConfig = {
  /** Base URL of the API as seen from the Next.js server, e.g. `http://localhost:5080`. */
  apiUrl: string;
  /** Origin the browser uses for this app, checked on every mutating BFF call (CSRF). */
  publicOrigin: string | undefined;
  /** API URL the browser connects to for the realtime hub (SignalR); none = no realtime, the pages poll. */
  apiPublicUrl: string | undefined;
  clients: Record<BffArea, ClientCredentials>;
  /** Absolute lifetime of a BFF session; the API session (idle timeout, revocation) ends it earlier. */
  sessionMaxAgeSeconds: number;
};

export function readBffConfig(env: Record<string, string | undefined> = process.env): BffConfig {
  return {
    apiUrl: (env.AUXILIA_API_URL ?? "http://localhost:5080").replace(/\/+$/, ""),
    publicOrigin: env.AUXILIA_PUBLIC_ORIGIN?.replace(/\/+$/, "") || undefined,
    apiPublicUrl: env.AUXILIA_API_PUBLIC_URL?.replace(/\/+$/, "") || undefined,
    clients: {
      tenant: {
        id: env.AUXILIA_WEB_CLIENT_ID ?? "web-bff",
        secret: env.AUXILIA_WEB_CLIENT_SECRET || undefined,
      },
      platform: {
        id: env.AUXILIA_CONSOLE_CLIENT_ID ?? "console",
        secret: env.AUXILIA_CONSOLE_CLIENT_SECRET || undefined,
      },
    },
    sessionMaxAgeSeconds: Number(env.AUXILIA_SESSION_MAX_AGE_SECONDS ?? 12 * 60 * 60),
  };
}
