import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { ConsoleShell } from "@/features/platform/components/console-shell";
import { consoleLanguages } from "@/features/platform/languages";
import { loadConsole } from "@/features/platform/server";
import { platformHref } from "@/lib/href";

/**
 * Signed-in area of the platform console (N02): only with a console session (System user, password + TOTP); the
 * user and the tenants come from the API. Tenant pages are `/platform/tenants/{slug}/…`: the slug in the path is the
 * selected tenant, whose technical endpoints the BFF calls with a tenant-scoped platform token (D-21).
 */
export default async function ConsoleLayout({ children }: LayoutProps<"/platform">) {
  const context = await loadConsole();
  if (!context.signedIn) {
    redirect(platformHref("/login"));
  }

  const t = await getTranslations();
  return (
    <ConsoleShell
      appName={t("app.platform.consoleName")}
      user={{ name: context.user.displayName, detail: context.user.email }}
      tenants={context.tenants.map(({ slug, displayName, status }) => ({
        slug,
        displayName,
        status,
      }))}
      languages={consoleLanguages(t)}
    >
      {children}
    </ConsoleShell>
  );
}
