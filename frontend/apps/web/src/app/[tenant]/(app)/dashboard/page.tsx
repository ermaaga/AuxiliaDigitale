import { getTranslations } from "next-intl/server";

import { serverApi } from "@/lib/api/server";

/** `/{tenant}/dashboard` (nav entry `dashboard` of Reporting): the landing page of every role (F01; widgets per role, B-23). */
export default async function DashboardPage() {
  const t = await getTranslations();
  const me = (await (await serverApi("tenant", "me")).json()) as { userName: string };

  return (
    <div className="flex flex-col gap-2">
      <h1 className="text-2xl font-semibold tracking-tight">
        {t("Welcome")}, {me.userName}
      </h1>
      <p className="text-muted-foreground">{t("app.shell.dashboardIntro")}</p>
    </div>
  );
}
