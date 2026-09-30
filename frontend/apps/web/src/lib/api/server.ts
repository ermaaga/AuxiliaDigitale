import { cookies, headers } from "next/headers";

import { callApi } from "@/lib/bff/api";
import { readBffConfig, type BffArea } from "@/lib/bff/config";
import { SESSION_COOKIE } from "@/lib/bff/session";
import { sessionStore } from "@/lib/bff/session-store";
import { withFreshAccessToken } from "@/lib/bff/tokens";

/**
 * API calls from Server Components and Server Functions (skill auxilia-frontend-feature): the session of the area
 * (tokens refreshed server-side; the cookie keeps the same id), the client application and the tenant. `tenant` is
 * used only without a session (public pages); with a session the session's tenant wins.
 */
export async function serverApi(
  area: BffArea,
  path: string,
  init: { method?: string; body?: BodyInit; headers?: HeadersInit; tenant?: string } = {},
): Promise<Response> {
  const config = readBffConfig();
  const store = sessionStore();
  const id = (await cookies()).get(SESSION_COOKIE[area])?.value;
  const stored = id ? await store.get(id) : undefined;
  const session =
    stored?.area === area ? await withFreshAccessToken(config, store, stored) : undefined;
  const incoming = new Request("http://internal", { headers: await headers() });

  return callApi(
    config,
    area,
    {
      method: init.method ?? "GET",
      path: path.replace(/^\/+/, ""),
      body: init.body,
      headers: init.headers,
      accessToken: session?.accessToken,
      tenant: session?.tenant ?? init.tenant,
    },
    incoming,
  );
}
