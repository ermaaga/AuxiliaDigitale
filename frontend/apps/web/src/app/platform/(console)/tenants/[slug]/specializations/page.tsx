import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantSpecializations } from "@/features/platform/components/access/tenant-specializations";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/specializations` (S-06, F12): specializations of the Client and Employee roles. */
export default async function TenantSpecializationsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/specializations">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.specializations.title")}
      description={t("app.platform.specializations.description")}
    >
      <TenantSpecializations slug={tenant.slug} />
    </TenantSection>
  );
}
