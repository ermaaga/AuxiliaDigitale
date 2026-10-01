"use client";

import * as React from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

import type { LanguageOption } from "@/components/shell/language-switcher";
import { ShellFrame, type ShellLink } from "@/components/shell/shell-frame";
import { PLATFORM_HOME, consoleTenantFromPath, platformHref, tenantConsoleHref } from "@/lib/href";

import { TenantSelector, type TenantOption } from "./tenant-selector";

/**
 * Frame of the platform console (N02): the tenant list, the pages of the selected tenant (the slug in the path) and
 * the tenant selector in the topbar; account menu with the System user. Signs out through the console BFF.
 */
export function ConsoleShell({
  appName,
  user,
  tenants,
  languages,
  children,
}: {
  appName: string;
  user: { name: string; detail: string };
  tenants: readonly TenantOption[];
  languages: readonly LanguageOption[];
  children: React.ReactNode;
}) {
  const t = useTranslations();
  const selected = consoleTenantFromPath(usePathname());
  const tenant = tenants.find((item) => item.slug === selected);

  const navigation: ShellLink[] = [
    { key: "tenants", label: t("app.platform.nav.tenants"), href: PLATFORM_HOME, icon: "building" },
    ...(tenant
      ? [
          {
            key: "tenant-overview",
            label: t("app.platform.nav.overview"),
            href: tenantConsoleHref(tenant.slug),
            icon: "gauge",
          },
          {
            key: "tenant-settings",
            label: t("app.platform.nav.settings"),
            href: tenantConsoleHref(tenant.slug, "/settings"),
            icon: "settings",
          },
          {
            key: "tenant-branding",
            label: t("app.platform.nav.branding"),
            href: tenantConsoleHref(tenant.slug, "/branding"),
            icon: "palette",
          },
          {
            key: "tenant-messaging",
            label: t("app.platform.nav.messaging"),
            href: tenantConsoleHref(tenant.slug, "/messaging"),
            icon: "mail",
          },
          {
            key: "tenant-grids",
            label: t("app.platform.nav.grids"),
            href: tenantConsoleHref(tenant.slug, "/grids"),
            icon: "table",
          },
          {
            key: "tenant-custom-fields",
            label: t("app.platform.nav.customFields"),
            href: tenantConsoleHref(tenant.slug, "/custom-fields"),
            icon: "sliders",
          },
          {
            key: "tenant-localization",
            label: t("app.platform.nav.localization"),
            href: tenantConsoleHref(tenant.slug, "/localization"),
            icon: "languages",
          },
          {
            key: "tenant-permissions",
            label: t("app.platform.nav.permissions"),
            href: tenantConsoleHref(tenant.slug, "/permissions"),
            icon: "key-round",
          },
          {
            key: "tenant-specializations",
            label: t("app.platform.nav.specializations"),
            href: tenantConsoleHref(tenant.slug, "/specializations"),
            icon: "award",
          },
        ]
      : []),
  ];

  return (
    <ShellFrame
      appName={appName}
      homeHref={PLATFORM_HOME}
      navigation={navigation}
      user={user}
      loginPath={platformHref("/login")}
      logoutUrl="/api/platform-auth/logout"
      languages={languages}
      topbar={<TenantSelector tenants={tenants} selected={tenant?.slug} />}
    >
      {children}
    </ShellFrame>
  );
}
