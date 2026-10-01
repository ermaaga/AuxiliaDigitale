"use client";

import * as React from "react";
import Image from "next/image";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { LogOutIcon, MenuIcon } from "lucide-react";
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

import { LanguageSwitcher, type LanguageOption } from "./language-switcher";
import { NavIcon } from "./nav-icon";
import { ThemeSwitcher } from "./theme-switcher";

/** A sidebar entry, already translated and resolved to its path. */
export type ShellLink = { key: string; label: string; href: string; icon: string };

/**
 * The frame shared by the tenant app and the platform console (skill auxilia-ui-design, F34): skip link, sidebar (a
 * drawer on mobile), topbar with the area's own controls, language, theme and the account menu. Sign-out and an ended
 * session (401 from any query, `auxilia:unauthenticated`) go to the sign-in page of the area.
 */
export function ShellFrame({
  appName,
  logoUrl,
  homeHref,
  navigation,
  user,
  loginPath,
  logoutUrl,
  languages,
  topbar,
  actions,
  accountItems,
  children,
}: {
  appName: string;
  /** Shown instead of the name in the sidebar; the name stays its alternative text. */
  logoUrl?: string;
  homeHref: string;
  navigation: readonly ShellLink[];
  user: { name: string; detail: string };
  loginPath: string;
  logoutUrl: string;
  languages: readonly LanguageOption[];
  /** Left of the topbar, after the menu button (e.g. the console's tenant selector). */
  topbar?: React.ReactNode;
  /** Right of the topbar, before language and theme (e.g. notifications). */
  actions?: React.ReactNode;
  /** Account menu entries above sign-out. */
  accountItems?: React.ReactNode;
  children: React.ReactNode;
}) {
  const t = useTranslations();
  const router = useRouter();
  const [menuOpen, setMenuOpen] = React.useState(false);

  React.useEffect(() => {
    const signOut = () => router.replace(loginPath);
    window.addEventListener(UNAUTHENTICATED_EVENT, signOut);
    return () => window.removeEventListener(UNAUTHENTICATED_EVENT, signOut);
  }, [router, loginPath]);

  async function logout() {
    try {
      await postToBff(logoutUrl);
    } finally {
      router.replace(loginPath);
      router.refresh();
    }
  }

  const nav = <NavLinks items={navigation} onNavigate={() => setMenuOpen(false)} />;

  return (
    <div className="flex min-h-dvh flex-1">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-background px-3 py-2 focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
      >
        {t("app.shell.skipToContent")}
      </a>
      <aside className="hidden w-64 shrink-0 flex-col border-r bg-sidebar text-sidebar-foreground md:flex">
        <Link href={homeHref} className="px-5 py-4 text-lg font-semibold tracking-tight">
          {logoUrl ? (
            <Image
              src={logoUrl}
              alt={appName}
              width={160}
              height={32}
              unoptimized
              className="h-8 w-auto max-w-40 object-contain"
            />
          ) : (
            appName
          )}
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
          {topbar ?? <span className="truncate font-semibold md:hidden">{appName}</span>}
          <div className="ml-auto flex items-center gap-1">
            {actions}
            <LanguageSwitcher languages={languages} />
            <ThemeSwitcher />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" aria-label={t("app.shell.accountMenu")}>
                  <Avatar className="size-8">
                    <AvatarFallback>{initials(user.name)}</AvatarFallback>
                  </Avatar>
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-56">
                <DropdownMenuLabel className="flex flex-col">
                  <span className="truncate">{user.name}</span>
                  <span className="text-xs font-normal text-muted-foreground">{user.detail}</span>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                {accountItems}
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

function NavLinks({ items, onNavigate }: { items: readonly ShellLink[]; onNavigate: () => void }) {
  const t = useTranslations();
  const pathname = usePathname();
  // The longest matching entry is the current one (`/platform/tenants/acme` highlights the tenant, not the list).
  const current = items
    .filter((item) => pathname === item.href || pathname.startsWith(`${item.href}/`))
    .sort((a, b) => b.href.length - a.href.length)[0]?.key;

  return (
    <nav aria-label={t("app.shell.navigation")} className="flex flex-col gap-1 px-3 py-2">
      {items.map((item) => {
        const active = item.key === current;
        return (
          <Link
            key={item.key}
            href={item.href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex min-h-9 items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
              active &&
                "bg-sidebar-primary text-sidebar-primary-foreground hover:bg-sidebar-primary hover:text-sidebar-primary-foreground",
            )}
          >
            <NavIcon name={item.icon} />
            {item.label}
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
