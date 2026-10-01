import Link from "next/link";
import { PlusIcon } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Button } from "@auxilia/ui/components/button";

import { TenantsTable } from "@/features/platform/components/tenants-table";
import type { PlatformTenant } from "@/features/platform/server";
import { serverApi } from "@/lib/api/server";
import { platformHref } from "@/lib/href";

/** `/platform/tenants` (N02): every tenant (archived ones through the status filter) and the creation of a new one. */
export default async function TenantsPage() {
  const t = await getTranslations();
  const response = await serverApi("platform", "platform/tenants?includeArchived=true");
  if (!response.ok) {
    throw new Error(`The API answered ${response.status} for the tenant list.`);
  }

  const tenants = (await response.json()) as PlatformTenant[];
  const title = t("app.platform.tenants.title");
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          <p className="text-sm text-muted-foreground">{t("app.platform.tenants.description")}</p>
        </div>
        <Button asChild>
          <Link href={platformHref("/tenants/new")}>
            <PlusIcon aria-hidden /> {t("app.platform.tenants.new")}
          </Link>
        </Button>
      </div>
      <TenantsTable tenants={tenants} title={title} />
    </div>
  );
}
