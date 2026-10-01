import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantBrandingEditor } from "@/features/platform/components/branding/tenant-branding";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/branding` (S-02, F23): name or logo, colours and login background of the tenant. */
export default async function TenantBrandingPage({
  params,
}: PageProps<"/platform/tenants/[slug]/branding">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.branding.title")}
      description={t("app.platform.branding.description")}
    >
      <TenantBrandingEditor slug={tenant.slug} />
    </TenantSection>
  );
}
