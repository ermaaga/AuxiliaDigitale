"use client";

import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Label } from "@auxilia/ui/components/label";

import { Combobox } from "@/components/combobox";
import { switchConsoleTenant } from "@/lib/href";

import { statusLabel } from "../tenant-status";

export type TenantOption = { slug: string; displayName: string; status: string };

/**
 * Tenant selector of the console topbar (N02): type-ahead over the tenants; choosing one opens the same page for that
 * tenant (`/platform/tenants/acme/x` → `/platform/tenants/beta/x`), or its overview from the list.
 */
export function TenantSelector({
  tenants,
  selected,
}: {
  tenants: readonly TenantOption[];
  selected: string | undefined;
}) {
  const t = useTranslations();
  const router = useRouter();
  const pathname = usePathname();

  const options = tenants.map((tenant) => ({
    value: tenant.slug,
    label: tenant.displayName,
    description: statusLabel(t, tenant.status, tenant.slug),
  }));

  return (
    <div className="flex min-w-0 items-center gap-2">
      <Label htmlFor="console-tenant" className="sr-only">
        {t("app.platform.tenantSelector.label")}
      </Label>
      <div className="w-48 sm:w-64">
        <Combobox
          id="console-tenant"
          options={options}
          value={selected}
          placeholder={t("app.platform.tenantSelector.placeholder")}
          onChange={(slug) => {
            if (slug && slug !== selected) {
              router.push(switchConsoleTenant(pathname, slug));
            }
          }}
        />
      </div>
    </div>
  );
}
