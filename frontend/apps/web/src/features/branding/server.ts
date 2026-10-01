import { cache } from "react";

import { publicApi } from "@/lib/api/server";

import { DEFAULT_BRANDING, type Branding } from "./branding";

/**
 * The public branding of a tenant for Server Components, once per request (tenant layout, shells, login pages). The
 * platform branding when the API cannot be reached: a page never fails because of its colours.
 */
export const loadBranding = cache(async (tenant: string): Promise<Branding> => {
  try {
    const response = await publicApi(tenant, "branding");
    return response.ok ? ((await response.json()) as Branding) : DEFAULT_BRANDING;
  } catch {
    return DEFAULT_BRANDING;
  }
});
