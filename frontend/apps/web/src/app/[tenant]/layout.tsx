import { headers } from "next/headers";
import { notFound } from "next/navigation";
import { BrandingStyle } from "@auxilia/ui/components/branding-style";

import { loadBranding } from "@/features/branding/server";
import { tenantFromPath } from "@/i18n/tenant";

/**
 * `/{tenant}/…` (ARCHITECTURE §14): anything that is not a valid tenant slug is a 404. The tenant colours (F23)
 * override the brand tokens for every page of the tenant, public and signed-in.
 */
export default async function TenantLayout({ children, params }: LayoutProps<"/[tenant]">) {
  const { tenant } = await params;
  if (tenantFromPath(`/${tenant}`) !== tenant) {
    notFound();
  }

  const [branding, nonce] = await Promise.all([
    loadBranding(tenant),
    headers().then((list) => list.get("x-nonce") ?? undefined),
  ]);
  return (
    <>
      <BrandingStyle
        branding={{
          primaryColor: branding.primaryColor,
          accentColor: branding.accentColor,
          themeFill: branding.themeFill,
        }}
        nonce={nonce}
      />
      {children}
    </>
  );
}
