"use client";

import * as React from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { BellIcon, LogOutIcon, MenuIcon, UserIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Avatar, AvatarFallback } from "@auxilia/ui/components/avatar";
import { Button } from "@auxilia/ui/components/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";
import { Popover, PopoverContent, PopoverTrigger } from "@auxilia/ui/components/popover";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@auxilia/ui/components/sheet";
import { cn } from "@auxilia/ui/lib/utils";

import { postToBff } from "@/lib/api/browser";
import { UNAUTHENTICATED_EVENT } from "@/lib/api/query-client";
import { tenantHref } from "@/lib/href";

import { LanguageSwitcher, type LanguageOption } from "./language-switcher";
import { NavIcon } from "./nav-icon";
import { ThemeSwitcher } from "./theme-switcher";

export type ShellNavigationItem = { key: string; labelKey: string; route: string; icon: string };

export type ShellUser = { userName: string; roles: readonly string[] };

/**
 * The tenant app frame (skill auxilia-ui-design, F34): sidebar from `/me/navigation` (a drawer on mobile), topbar with
 * notifications (placeholder until B-19/B-21), language, theme and the account menu; an ended session (401 from any
 * query, `auxilia:unauthenticated`) goes back to the sign-in page.
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
  const router = useRouter();
  const [menuOpen, setMenuOpen] = React.useState(false);
  const loginPath = tenantHref(tenant, "/login");

  React.useEffect(() => {
    const signOut = () => router.replace(loginPath);
    window.addEventListener(UNAUTHENTICATED_EVENT, signOut);
    return () => window.removeEventListener(UNAUTHENTICATED_EVENT, signOut);
  }, [router, loginPath]);

  async function logout() {
    try {
      await postToBff("/api/auth/logout");
    } finally {
      router.replace(loginPath);
      router.refresh();
    }
  }

  const nav = <NavLinks tenant={tenant} items={navigation} onNavigate={() => setMenuOpen(false)} />;

  return (
    <div className="flex min-h-dvh flex-1">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-background px-3 py-2 focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
      >
        {t("app.shell.skipToContent")}
      </a>
      <aside className="hidden w-64 shrink-0 flex-col border-r bg-sidebar text-sidebar-foreground md:flex">
        <Link href={tenantHref(tenant)} className="px-5 py-4 text-lg font-semibold tracking-tight">
          {appName}
        </Link>
        {nav}
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-2 border-b bg-background/95 px-3 backdrop-blur sm:px-4">
          <Sheet open={menuOpen} onOpenChange={setMenuOpen}>
            <SheetTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="md:hidden"
                aria-label={t("app.shell.openMenu")}
              >
                <MenuIcon aria-hidden />
              </Button>
            </SheetTrigger>
            <SheetContent
              side="left"
              className="w-72 bg-sidebar p-0 text-sidebar-foreground"
              closeLabel={t("Close")}
            >
              <SheetHeader>
                <SheetTitle>{appName}</SheetTitle>
              </SheetHeader>
              {nav}
            </SheetContent>
          </Sheet>
          <span className="truncate font-semibold md:hidden">{appName}</span>
          <div className="ml-auto flex items-center gap-1">
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
            <LanguageSwitcher languages={languages} />
            <ThemeSwitcher />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" aria-label={t("app.shell.accountMenu")}>
                  <Avatar className="size-8">
                    <AvatarFallback>{initials(user.userName)}</AvatarFallback>
                  </Avatar>
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-56">
                <DropdownMenuLabel className="flex flex-col">
                  <span className="truncate">{user.userName}</span>
                  <span className="text-xs font-normal text-muted-foreground">
                    {user.roles.join(", ")}
                  </span>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem disabled>
                  <UserIcon aria-hidden /> {t("MyProfile")}
                </DropdownMenuItem>
                <DropdownMenuItem onSelect={() => void logout()}>
                  <LogOutIcon aria-hidden /> {t("Logout")}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </header>
        <main id="main" className="flex-1 p-4 sm:p-6">
          {children}
        </main>
      </div>
    </div>
  );
}

function NavLinks({
  tenant,
  items,
  onNavigate,
}: {
  tenant: string;
  items: readonly ShellNavigationItem[];
  onNavigate: () => void;
}) {
  const t = useTranslations();
  const pathname = usePathname();

  return (
    <nav aria-label={t("app.shell.navigation")} className="flex flex-col gap-1 px-3 py-2">
      {items.map((item) => {
        const href = tenantHref(tenant, item.route);
        const active = pathname === href || (item.route !== "/" && pathname.startsWith(`${href}/`));
        return (
          <Link
            key={item.key}
            href={href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex min-h-9 items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
              active &&
                "bg-sidebar-primary text-sidebar-primary-foreground hover:bg-sidebar-primary hover:text-sidebar-primary-foreground",
            )}
          >
            <NavIcon name={item.icon} />
            {t.has(item.labelKey) ? t(item.labelKey) : item.labelKey}
          </Link>
        );
      })}
    </nav>
  );
}

/** Up to two initials of the user name (`mario.rossi` → `MR`). */
export function initials(userName: string): string {
  const parts = userName.split(/[\s._@-]+/).filter(Boolean);
  return (
    parts.length > 1 ? parts[0]![0]! + parts[1]![0]! : (parts[0] ?? "?").slice(0, 2)
  ).toUpperCase();
}
