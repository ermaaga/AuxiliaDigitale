import { cache } from "react";
import { redirect } from "next/navigation";
import type { components } from "@auxilia/api-client";

import { currentSession, serverApi } from "@/lib/api/server";
import { platformHref } from "@/lib/href";

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

export type PlatformTenantDetail = components["schemas"]["PlatformTenantDetailResponse"];

/** A tenant of the console page `/platform/tenants/{slug}/…`; `undefined` when the API does not know it (404). */
export const loadConsoleTenant = cache(
  async (slug: string): Promise<PlatformTenantDetail | undefined> => {
    const response = await serverApi("platform", `platform/tenants/${encodeURIComponent(slug)}`);
    if (response.status === 404) {
      return undefined;
    }

    // The session ended while the page rendered (the layout redirects too): sign in again, not an error.
    if (response.status === 401) {
      redirect(platformHref("/login"));
    }

    if (!response.ok) {
      throw new Error(`The API answered ${response.status} for tenant ${slug}.`);
    }

    return (await response.json()) as PlatformTenantDetail;
  },
);
