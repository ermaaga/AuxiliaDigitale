import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { ImportDetail } from "@/features/platform/components/imports/import-detail";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/imports/{id}` (S-08, F19): one import with the preview of its rows, confirm or cancel. */
export default async function TenantImportPage({
  params,
}: PageProps<"/platform/tenants/[slug]/imports/[id]">) {
  const { slug, id } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.imports.detailTitle")}
      description={t("app.platform.imports.detailDescription")}
    >
      <ImportDetail slug={tenant.slug} id={id} />
    </TenantSection>
  );
}
