import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { TenantMessaging } from "@/features/platform/components/messaging/tenant-messaging";
import { TenantSection } from "@/features/platform/components/tenant-section";
import { loadConsoleTenant } from "@/features/platform/server";

/** `/platform/tenants/{slug}/messaging` (S-03, N03): sending accounts, sender rules, test send and outbound log. */
export default async function TenantMessagingPage({
  params,
}: PageProps<"/platform/tenants/[slug]/messaging">) {
  const { slug } = await params;
  const tenant = await loadConsoleTenant(decodeURIComponent(slug));
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <TenantSection
      tenant={tenant}
      title={t("app.platform.messaging.title")}
      description={t("app.platform.messaging.description")}
    >
      <TenantMessaging slug={tenant.slug} />
    </TenantSection>
  );
}
