import { getTranslations } from "next-intl/server";

import { CreateTenantForm } from "@/features/platform/components/create-tenant-form";

/** `/platform/tenants/new` (N02): creation of a tenant with asynchronous provisioning. */
export default async function NewTenantPage() {
  const t = await getTranslations();
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">{t("app.platform.create.title")}</h1>
        <p className="text-sm text-muted-foreground">{t("app.platform.create.description")}</p>
      </div>
      <CreateTenantForm />
    </div>
  );
}
