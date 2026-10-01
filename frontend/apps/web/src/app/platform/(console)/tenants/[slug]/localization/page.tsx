import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantLocalization } from "@/features/platform/components/localization/tenant-localization";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/localization` (S-05, F24): labels and translations of the tenant. */
export default async function TenantLocalizationPage({
  params,
}: PageProps<"/platform/tenants/[slug]/localization">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.localization.title")}
      description={t("app.platform.localization.description")}
    >
      <TenantLocalization slug={tenant.slug} />
    </TenantSection>
  );
}
