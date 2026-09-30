import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Badge } from "@auxilia/ui/components/badge";
import { Card, CardContent } from "@auxilia/ui/components/card";

import { TenantLanguages } from "@/features/platform/components/tenant-languages";
import { statusLabel } from "@/features/platform/tenant-status";
import { loadConsole } from "@/features/platform/server";

/**
 * `/platform/tenants/{slug}` (N02): overview of the selected tenant. The technical sections (settings, branding,
 * translations, messaging, permissions, jobs) are added under this path by S-01…S-08.
 */
export default async function TenantOverviewPage({
  params,
}: PageProps<"/platform/tenants/[slug]">) {
  const { slug } = await params;
  const context = await loadConsole();
  const tenant = context.signedIn
    ? context.tenants.find((item) => item.slug === decodeURIComponent(slug))
    : undefined;
  if (!tenant) {
    notFound();
  }

  const t = await getTranslations();
  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{tenant.displayName}</h1>
        <p className="text-sm text-muted-foreground">{t("app.platform.tenant.description")}</p>
      </div>
      <Card>
        <CardContent>
          <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-3">
            <div className="flex flex-col gap-1">
              <dt className="text-sm text-muted-foreground">{t("app.platform.tenants.slug")}</dt>
              <dd>
                <code>{tenant.slug}</code>
              </dd>
            </div>
            <div className="flex flex-col gap-1">
              <dt className="text-sm text-muted-foreground">{t("Status")}</dt>
              <dd>
                <Badge variant="secondary">{statusLabel(t, tenant.status)}</Badge>
              </dd>
            </div>
            <div className="flex flex-col gap-1">
              <dt className="text-sm text-muted-foreground">
                {t("app.platform.tenants.schemaVersion")}
              </dt>
              <dd className="text-sm break-all">{tenant.schemaVersion ?? "—"}</dd>
            </div>
          </dl>
        </CardContent>
      </Card>
      <TenantLanguages tenant={tenant.slug} />
    </div>
  );
}
