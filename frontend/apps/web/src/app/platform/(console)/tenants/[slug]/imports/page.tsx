import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantImports } from "@/features/platform/components/imports/tenant-imports";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/imports` (S-08, F19, D-18): import types, new imports and their progress. */
export default async function TenantImportsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/imports">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.imports.title")}
      description={t("app.platform.imports.description")}
    >
      <TenantImports slug={tenant.slug} />
    </TenantSection>
  );
}
