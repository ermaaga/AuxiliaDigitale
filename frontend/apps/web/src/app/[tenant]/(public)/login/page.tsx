import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { LoginForm } from "@/features/auth/components/login-form";
import { currentSession } from "@/lib/api/server";
import { safeNextPath } from "@/lib/href";

import { PublicPage } from "../public-page";

/** `/{tenant}/login` (F01): a signed-in user of this tenant goes straight to the app. */
export default async function LoginPage({ params, searchParams }: PageProps<"/[tenant]/login">) {
  const { tenant } = await params;
  const query = await searchParams;
  const next = safeNextPath(tenant, typeof query.next === "string" ? query.next : undefined);
  if ((await currentSession("tenant"))?.tenant === tenant) {
    redirect(next);
  }

  const t = await getTranslations();
  return (
    <PublicPage tenant={tenant} title={t("app.auth.login.title")}>
      {(methods) => <LoginForm tenant={tenant} methods={methods} next={next} />}
    </PublicPage>
  );
}
