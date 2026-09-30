import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Button } from "@auxilia/ui/components/button";

import { PLATFORM_HOME } from "@/lib/href";

/** 404 inside the console (e.g. an unknown tenant slug): back to the tenant list, the shell stays. */
export default async function ConsoleNotFound() {
  const t = await getTranslations();
  return (
    <div className="flex flex-col items-start gap-3">
      <h1 className="text-2xl font-semibold tracking-tight">{t("app.shell.notFound.title")}</h1>
      <p className="text-muted-foreground">{t("app.shell.notFound.description")}</p>
      <Button asChild variant="outline">
        <Link href={PLATFORM_HOME}>{t("app.platform.nav.tenants")}</Link>
      </Button>
    </div>
  );
}
