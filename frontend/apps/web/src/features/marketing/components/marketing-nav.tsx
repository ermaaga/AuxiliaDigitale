"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { cn } from "@auxilia/ui/lib/utils";

import { tenantHref } from "@/lib/href";

const SECTIONS = ["campaigns", "segments", "lists", "templates", "suppressions"] as const;

/** The sections of the marketing pages (N01): campaigns, segments, static lists, templates, suppressions. */
export function MarketingNav({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const pathname = usePathname();
  return (
    <nav aria-label={t("app.marketing.sections")} className="flex flex-wrap gap-1 border-b pb-2">
      {SECTIONS.map((section) => {
        const href = tenantHref(tenant, `/marketing/${section}`);
        const active = pathname.startsWith(href);
        return (
          <Link
            key={section}
            href={href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "rounded-md px-3 py-1.5 text-sm font-medium underline-offset-4 hover:underline",
              active ? "bg-secondary text-secondary-foreground" : "text-muted-foreground",
            )}
          >
            {t(`app.marketing.nav.${section}`)}
          </Link>
        );
      })}
    </nav>
  );
}
