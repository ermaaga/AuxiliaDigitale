import { redirect } from "next/navigation";

import { AppShell, type ShellNavigationItem, type ShellUser } from "@/components/shell/app-shell";
import { brandIdentity } from "@/features/branding/branding";
import { loadBranding } from "@/features/branding/server";
import { tenantLanguages } from "@/i18n/bundles";
import { currentSession, serverApi } from "@/lib/api/server";
import { PermissionsProvider } from "@/lib/permissions";
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

  const [meResponse, navigationResponse, profileResponse] = await Promise.all([
    serverApi("tenant", "me"),
    serverApi("tenant", "me/navigation"),
    serverApi("tenant", "me/profile"),
  ]);
  if (meResponse.status === 401 || navigationResponse.status === 401) {
    redirect(login);
  }

  if (!meResponse.ok || !navigationResponse.ok) {
    throw new Error(
      `The API answered ${meResponse.status}/${navigationResponse.status} for the user of ${tenant}.`,
    );
  }

  const me = (await meResponse.json()) as {
    id: string;
    userName: string;
    roles: string[];
    permissions: string[];
  };
  // The full name and the picture come from the profile (F04); without one the user name and the initials.
  const profile = profileResponse.ok
    ? ((await profileResponse.json()) as {
        firstName: string;
        lastName: string;
        imageVersion: string | null;
      })
    : undefined;
  const user: ShellUser = {
    id: me.id,
    name: profile ? `${profile.firstName} ${profile.lastName}`.trim() || me.userName : me.userName,
    roles: me.roles,
    imageVersion: profile?.imageVersion,
  };
  const navigation = (
    (await navigationResponse.json()) as Array<ShellNavigationItem & { order: number | string }>
  )
    .sort((a, b) => Number(a.order) - Number(b.order))
    .map(({ key, labelKey, route, icon }) => ({ key, labelKey, route, icon }));
  const [languages, branding] = await Promise.all([
    tenantLanguages(tenant).then((list) => list ?? []),
    loadBranding(tenant),
  ]);
  const identity = brandIdentity(tenant, branding);

  return (
    <PermissionsProvider roles={me.roles} permissions={me.permissions}>
      <AppShell
        tenant={tenant}
        appName={identity.appName}
        logoUrl={identity.logoUrl}
        navigation={navigation}
        user={user}
        languages={languages}
      >
        {children}
      </AppShell>
    </PermissionsProvider>
  );
}
