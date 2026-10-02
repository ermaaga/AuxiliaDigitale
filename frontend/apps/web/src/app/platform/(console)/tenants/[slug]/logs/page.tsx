import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantLogs } from "@/features/platform/components/logs/tenant-logs";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/logs` (S-07, F25, D-28): the tenant's log files and its temporary debug level. */
export default async function TenantLogsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/logs">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.logs.title")}
      description={t("app.platform.logs.description")}
      anyStatus
    >
      <TenantLogs slug={tenant.slug} />
    </TenantSection>
  );
}
