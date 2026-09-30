import { cache } from "react";
import type { components } from "@auxilia/api-client";

import { currentSession, serverApi } from "@/lib/api/server";

export type PlatformUser = components["schemas"]["PlatformMeResponse"];
export type PlatformTenant = components["schemas"]["PlatformTenantResponse"];

export type ConsoleContext =
  { signedIn: false } | { signedIn: true; user: PlatformUser; tenants: PlatformTenant[] };

/**
 * The System user and the tenants of the console session, loaded once per request (layout and pages share it).
 * No session, or a session the API no longer accepts (401), means signed out; any other failure is an error.
 */
export const loadConsole = cache(async (): Promise<ConsoleContext> => {
  if (!(await currentSession("platform"))) {
    return { signedIn: false };
  }

  const [me, tenants] = await Promise.all([
    serverApi("platform", "platform/me"),
    serverApi("platform", "platform/tenants"),
  ]);
  if (me.status === 401 || tenants.status === 401) {
    return { signedIn: false };
  }

  if (!me.ok || !tenants.ok) {
    throw new Error(`The API answered ${me.status}/${tenants.status} for the console user.`);
  }

  return {
    signedIn: true,
    user: (await me.json()) as PlatformUser,
    tenants: (await tenants.json()) as PlatformTenant[],
  };
});
