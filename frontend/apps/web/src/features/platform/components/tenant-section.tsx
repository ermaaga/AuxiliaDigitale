import * as React from "react";
import { getTranslations } from "next-intl/server";

import type { PlatformTenantDetail } from "../server";

/**
 * Heading of a technical page of a tenant (settings, branding, …): page title, tenant name and slug, description.
 * The technical endpoints serve active tenants only: otherwise a notice instead of the editor.
 */
export async function TenantSection({
  tenant,
  title,
  description,
  children,
}: {
  tenant: PlatformTenantDetail;
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  const t = await getTranslations();
  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-4">
      <header className="flex flex-col gap-1">
        <p className="text-sm text-muted-foreground">
          {tenant.displayName} · <code>{tenant.slug}</code>
        </p>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="text-sm text-muted-foreground">{description}</p>
      </header>
      {tenant.status === "Active" ? (
        children
      ) : (
        <p role="status" className="rounded-md border p-4 text-sm">
          {t("app.platform.settings.unavailable")}
        </p>
      )}
    </div>
  );
}
