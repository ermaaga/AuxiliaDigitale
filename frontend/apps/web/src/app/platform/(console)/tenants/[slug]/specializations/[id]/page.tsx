import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { SpecializationMembers } from "@/features/platform/components/access/specialization-members";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/specializations/{id}` (S-06, F12): the users holding a specialization. */
export default async function SpecializationMembersPage({
  params,
}: PageProps<"/platform/tenants/[slug]/specializations/[id]">) {
  const { slug, id } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.specializations.membersTitle")}
      description={t("app.platform.specializations.membersDescription")}
    >
      <SpecializationMembers slug={tenant.slug} id={decodeURIComponent(id)} />
    </TenantSection>
  );
}
