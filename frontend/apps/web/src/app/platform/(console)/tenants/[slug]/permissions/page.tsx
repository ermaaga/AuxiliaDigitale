import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantPermissions } from "@/features/platform/components/access/tenant-permissions";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/permissions` (S-06, F22): what each tenant role may do. */
export default async function TenantPermissionsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/permissions">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.permissions.title")}
      description={t("app.platform.permissions.description")}
    >
      <TenantPermissions slug={tenant.slug} />
    </TenantSection>
  );
}
