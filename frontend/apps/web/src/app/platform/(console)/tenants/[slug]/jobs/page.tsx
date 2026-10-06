import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantJobs } from "@/features/platform/components/jobs/tenant-jobs";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/jobs` (N02, D-15): the recurring jobs of the tenant, their runs and "run now". */
export default async function TenantJobsPage({
  params,
}: PageProps<"/platform/tenants/[slug]/jobs">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.jobs.title")}
      description={t("app.platform.jobs.description")}
    >
      <TenantJobs slug={tenant.slug} />
    </TenantSection>
  );
}
