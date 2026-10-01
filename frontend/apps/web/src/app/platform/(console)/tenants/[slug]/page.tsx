import { notFound } from "next/navigation";

import { TenantOverview } from "@/features/platform/components/tenant/tenant-overview";
import type { TenantDetail } from "@/features/platform/tenant-api";
import { serverApi } from "@/lib/api/server";

/**
 * `/platform/tenants/{slug}` (N02): the tenant (archived ones too, read-only) with its technical administration. The
 * technical sections (settings, branding, translations, messaging, permissions, jobs) are added by S-02…S-08.
 */
export default async function TenantPage({ params }: PageProps<"/platform/tenants/[slug]">) {
  const { slug } = await params;
  const response = await serverApi(
    "platform",
    `platform/tenants/${encodeURIComponent(decodeURIComponent(slug))}`,
  );
  if (response.status === 404) {
    notFound();
  }

  if (!response.ok) {
    throw new Error(`The API answered ${response.status} for tenant ${slug}.`);
  }

  return <TenantOverview initial={(await response.json()) as TenantDetail} />;
}
