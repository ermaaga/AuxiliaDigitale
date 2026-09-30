import { getTranslations } from "next-intl/server";

import { TenantsTable } from "@/features/platform/components/tenants-table";
import { loadConsole } from "@/features/platform/server";

/** `/platform/tenants` (N02): every tenant of the platform; creation and lifecycle actions come with S-01. */
export default async function TenantsPage() {
  const context = await loadConsole();
  const t = await getTranslations();
  const title = t("app.platform.tenants.title");
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="text-sm text-muted-foreground">{t("app.platform.tenants.description")}</p>
      </div>
      <TenantsTable tenants={context.signedIn ? context.tenants : []} title={title} />
    </div>
  );
}
