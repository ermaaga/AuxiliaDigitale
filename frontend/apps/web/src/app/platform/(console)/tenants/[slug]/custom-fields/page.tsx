import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantCustomFields } from "@/features/platform/components/customization/tenant-custom-fields";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/custom-fields` (S-04, F20): custom fields of the tenant's entities. */
export default async function TenantCustomFieldsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/custom-fields">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.customFields.title")}
      description={t("app.platform.customFields.description")}
    >
      <TenantCustomFields slug={tenant.slug} />
    </TenantSection>
  );
}
