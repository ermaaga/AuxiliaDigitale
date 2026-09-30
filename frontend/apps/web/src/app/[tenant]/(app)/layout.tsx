import { redirect } from "next/navigation";

import { AppShell, type ShellNavigationItem, type ShellUser } from "@/components/shell/app-shell";
import { tenantLanguages } from "@/i18n/bundles";
import { currentSession, serverApi } from "@/lib/api/server";
import { DEFAULT_APP_NAME } from "@/lib/app";
import { tenantHref } from "@/lib/href";

/**
 * Signed-in area of a tenant (skill auxilia-frontend-feature): the user and the navigation come from the API
 * (`/me`, `/me/navigation`), never from the client; without a session of this tenant, or when the API says the
 * session is over, the sign-in page. The API still enforces every permission.
 */
export default async function AppLayout({ children, params }: LayoutProps<"/[tenant]">) {
  const { tenant } = await params;
  const login = tenantHref(tenant, "/login");
  if ((await currentSession("tenant"))?.tenant !== tenant) {
    redirect(login);
  }

  const [meResponse, navigationResponse] = await Promise.all([
    serverApi("tenant", "me"),
    serverApi("tenant", "me/navigation"),
  ]);
  if (meResponse.status === 401 || navigationResponse.status === 401) {
    redirect(login);
  }

  if (!meResponse.ok || !navigationResponse.ok) {
    throw new Error(
      `The API answered ${meResponse.status}/${navigationResponse.status} for the user of ${tenant}.`,
    );
  }

  const me = (await meResponse.json()) as ShellUser;
  const navigation = (
    (await navigationResponse.json()) as Array<ShellNavigationItem & { order: number | string }>
  )
    .sort((a, b) => Number(a.order) - Number(b.order))
    .map(({ key, labelKey, route, icon }) => ({ key, labelKey, route, icon }));
  const languages = (await tenantLanguages(tenant)) ?? [];

  return (
    <AppShell
      tenant={tenant}
      appName={DEFAULT_APP_NAME}
      navigation={navigation}
      user={{ userName: me.userName, roles: me.roles }}
      languages={languages}
    >
      {children}
    </AppShell>
  );
}
