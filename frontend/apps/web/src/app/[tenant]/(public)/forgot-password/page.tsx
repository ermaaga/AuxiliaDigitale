import { getTranslations } from "next-intl/server";

import { ForgotPasswordForm } from "@/features/auth/components/forgot-password-form";

import { PublicPage } from "../public-page";

/** `/{tenant}/forgot-password` (F01): request a reset link. */
export default async function ForgotPasswordPage({
  params,
}: PageProps<"/[tenant]/forgot-password">) {
  const { tenant } = await params;
  const t = await getTranslations();
  return (
    <PublicPage
      tenant={tenant}
      title={t("ForgotPasswordTitle")}
      description={t("app.auth.forgot.description")}
    >
      {() => <ForgotPasswordForm tenant={tenant} />}
    </PublicPage>
  );
}
