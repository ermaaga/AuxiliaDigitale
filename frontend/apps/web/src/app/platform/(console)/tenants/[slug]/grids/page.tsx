import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantGrids } from "@/features/platform/components/customization/tenant-grids";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/grids` (S-04, F21): columns of the tenant app grids per role. */
export default async function TenantGridsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/grids">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.grids.title")}
      description={t("app.platform.grids.description")}
    >
      <TenantGrids slug={tenant.slug} />
    </TenantSection>
  );
}
