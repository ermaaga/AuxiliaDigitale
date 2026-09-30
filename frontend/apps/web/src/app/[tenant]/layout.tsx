import { notFound } from "next/navigation";

import { tenantFromPath } from "@/i18n/tenant";

/** `/{tenant}/…` (ARCHITECTURE §14): anything that is not a valid tenant slug is a 404. */
export default async function TenantLayout({ children, params }: LayoutProps<"/[tenant]">) {
  const { tenant } = await params;
  if (tenantFromPath(`/${tenant}`) !== tenant) {
    notFound();
  }

  return children;
}
