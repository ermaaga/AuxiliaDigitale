import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { PlatformLoginForm } from "@/features/platform/components/platform-login-form";
import { PlatformPublicPage } from "@/features/platform/components/platform-public-page";
import { currentSession } from "@/lib/api/server";
import { safePlatformNextPath } from "@/lib/href";

/** `/platform/login` (N02, D-22): System sign-in; an open console session goes straight to the console. */
export default async function PlatformLoginPage({ searchParams }: PageProps<"/platform/login">) {
  const { next } = await searchParams;
  const target = safePlatformNextPath(typeof next === "string" ? next : undefined);
  if (await currentSession("platform")) {
    redirect(target);
  }

  const t = await getTranslations();
  return (
    <PlatformPublicPage
      title={t("app.platform.login.title")}
      description={t("app.platform.login.description")}
    >
      <PlatformLoginForm next={target} />
    </PlatformPublicPage>
  );
}
