import { getTranslations } from "next-intl/server";

import { DashboardView } from "@/features/dashboard";
import { serverApi } from "@/lib/api/server";

/** `/{tenant}/dashboard` (nav entry `dashboard`, F27, Q44): the landing page of every role with its widgets. */
export default async function DashboardPage({ params }: PageProps<"/[tenant]/dashboard">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const me = (await (await serverApi("tenant", "me")).json()) as { userName: string };
  const profile = await serverApi("tenant", "me/profile");
  const name = profile.ok
    ? ((await profile.json()) as { firstName: string }).firstName || me.userName
    : me.userName;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t("Welcome")}, {name}
        </h1>
        <p className="text-muted-foreground">{t("app.shell.dashboardIntro")}</p>
      </div>
      <DashboardView tenant={tenant} />
    </div>
  );
}
