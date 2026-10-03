"use client";

import * as React from "react";
import Link from "next/link";
import { BellIcon, UserIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { DropdownMenuItem } from "@auxilia/ui/components/dropdown-menu";
import { Popover, PopoverContent, PopoverTrigger } from "@auxilia/ui/components/popover";

import { userImageUrl } from "@/components/user-avatar";
import { createBffClient } from "@/lib/api/client";
import { tenantHref } from "@/lib/href";

import type { LanguageOption } from "./language-switcher";
import { ShellFrame } from "./shell-frame";

export { initials } from "./shell-frame";

export type ShellNavigationItem = { key: string; labelKey: string; route: string; icon: string };

/** `name`: the person's full name (the user name without a profile); `imageVersion`: the picture hash (F04). */
export type ShellUser = {
  id: string;
  name: string;
  roles: readonly string[];
  imageVersion?: string | null;
};

/**
 * The tenant app frame (skill auxilia-ui-design, F34): sidebar from `/me/navigation`, notifications (placeholder until
 * B-19/B-21) and the account menu of the tenant user with the profile picture and the link to the profile (F04); the
 * language and theme chosen in the topbar are saved in the profile too, so they hold at the next sign-in (F04, Q35).
 * The rest is the shared {@link ShellFrame}.
 */
export function AppShell({
  tenant,
  appName,
  logoUrl,
  navigation,
  user,
  languages,
  children,
}: {
  tenant: string;
  appName: string;
  /** Tenant logo instead of the name (F23). */
  logoUrl?: string;
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
      logoUrl={logoUrl}
      homeHref={tenantHref(tenant)}
      navigation={links}
      user={{
        name: user.name,
        detail: user.roles.join(", "),
        imageUrl: userImageUrl(user.id, user.imageVersion),
      }}
      loginPath={tenantHref(tenant, "/login")}
      logoutUrl="/api/auth/logout"
      languages={languages}
      onLanguageChange={saveLanguage}
      onThemeChange={saveTheme}
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
        <DropdownMenuItem asChild>
          <Link href={tenantHref(tenant, "/profile")}>
            <UserIcon aria-hidden /> {t("MyProfile")}
          </Link>
        </DropdownMenuItem>
      }
    >
      {children}
    </ShellFrame>
  );
}

/** Saves the language chosen in the topbar in the profile; the cookie already switched this browser. */
async function saveLanguage(code: string) {
  // A failure leaves the choice in this browser only (openapi-fetch reports it in the result, never throws).
  await createBffClient("tenant")
    .PUT("/api/v1/me/language", { body: { languageCode: code } })
    .catch(() => undefined);
}

const API_THEMES: Record<string, string> = { system: "System", light: "Light", dark: "Dark" };

/** Saves the theme chosen in the topbar in the profile (Q35); fire and forget. */
function saveTheme(name: string) {
  const theme = API_THEMES[name];
  if (theme) {
    void createBffClient("tenant")
      .PUT("/api/v1/me/preferences", { body: { theme } })
      .catch(() => undefined);
  }
}
