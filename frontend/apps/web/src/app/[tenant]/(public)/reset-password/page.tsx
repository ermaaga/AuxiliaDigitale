import { getTranslations } from "next-intl/server";

import { SetPasswordForm } from "@/features/auth/components/set-password-form";

import { PublicPage } from "../public-page";

/** `/{tenant}/reset-password?token=…` (link of the reset e-mail, `auth.appBaseUrl`). */
export default async function ResetPasswordPage({
  params,
  searchParams,
}: PageProps<"/[tenant]/reset-password">) {
  const { tenant } = await params;
  const { token } = await searchParams;
  const t = await getTranslations();
  return (
    <PublicPage tenant={tenant} title={t("ResetPasswordTitle")}>
      {() => (
        <SetPasswordForm
          tenant={tenant}
          token={typeof token === "string" ? token : undefined}
          kind="reset"
        />
      )}
    </PublicPage>
  );
}
