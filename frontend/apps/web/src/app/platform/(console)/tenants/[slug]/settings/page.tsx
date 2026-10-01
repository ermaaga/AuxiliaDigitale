import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantSettings } from "@/features/platform/components/settings/tenant-settings";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/settings` (S-02, F23): the typed settings of the tenant. */
export default async function TenantSettingsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/settings">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.settings.title")}
      description={t("app.platform.settings.description")}
    >
      <TenantSettings slug={tenant.slug} />
    </TenantSection>
  );
}
