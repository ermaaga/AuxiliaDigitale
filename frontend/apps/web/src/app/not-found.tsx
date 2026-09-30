import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Button } from "@auxilia/ui/components/button";

/** 404 of the whole app (unknown tenant, route or hidden module), translated. */
export default async function NotFound() {
  const t = await getTranslations();
  return (
    <main
      id="main"
      className="flex flex-1 flex-col items-center justify-center gap-4 p-6 text-center"
    >
      <p className="text-5xl font-semibold text-muted-foreground">404</p>
      <h1 className="text-2xl font-semibold tracking-tight">{t("app.shell.notFound.title")}</h1>
      <p className="max-w-md text-muted-foreground">{t("app.shell.notFound.description")}</p>
      <Button asChild variant="outline">
        <Link href="/">{t("Home")}</Link>
      </Button>
    </main>
  );
}
