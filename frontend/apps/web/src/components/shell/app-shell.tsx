"use client";

import * as React from "react";
import { BellIcon, UserIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { DropdownMenuItem } from "@auxilia/ui/components/dropdown-menu";
import { Popover, PopoverContent, PopoverTrigger } from "@auxilia/ui/components/popover";

import { tenantHref } from "@/lib/href";

import type { LanguageOption } from "./language-switcher";
import { ShellFrame } from "./shell-frame";

export { initials } from "./shell-frame";

export type ShellNavigationItem = { key: string; labelKey: string; route: string; icon: string };

export type ShellUser = { userName: string; roles: readonly string[] };

/**
 * The tenant app frame (skill auxilia-ui-design, F34): sidebar from `/me/navigation`, notifications (placeholder until
 * B-19/B-21) and the account menu of the tenant user; the rest is the shared {@link ShellFrame}.
 */
export function AppShell({
  tenant,
  appName,
  navigation,
  user,
  languages,
  children,
}: {
  tenant: string;
  appName: string;
  navigation: readonly ShellNavigationItem[];
  user: ShellUser;
  languages: readonly LanguageOption[];
  children: React.ReactNode;
}) {
  const t = useTranslations();
  const links = navigation.map((item) => ({
    key: item.key,
    label: t.has(item.labelKey) ? t(item.labelKey) : item.labelKey,
    href: tenantHref(tenant, item.route),
    icon: item.icon,
  }));

  return (
    <ShellFrame
      appName={appName}
      homeHref={tenantHref(tenant)}
      navigation={links}
      user={{ name: user.userName, detail: user.roles.join(", ") }}
      loginPath={tenantHref(tenant, "/login")}
      logoutUrl="/api/auth/logout"
      languages={languages}
      actions={
        <Popover>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="icon" aria-label={t("Notifications")}>
              <BellIcon aria-hidden />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-72">
            <p className="text-sm font-medium">{t("Notifications")}</p>
            <p className="mt-2 text-sm text-muted-foreground">
              {t("app.shell.notificationsEmpty")}
            </p>
          </PopoverContent>
        </Popover>
      }
      accountItems={
        <DropdownMenuItem disabled>
          <UserIcon aria-hidden /> {t("MyProfile")}
        </DropdownMenuItem>
      }
    >
      {children}
    </ShellFrame>
  );
}
