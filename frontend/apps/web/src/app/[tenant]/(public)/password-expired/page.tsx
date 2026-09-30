import { getTranslations } from "next-intl/server";

import { ExpiredPasswordForm } from "@/features/auth/components/expired-password-form";

import { PublicPage } from "../public-page";

/** `/{tenant}/password-expired` (F35): reached from the sign-in when the API answers `AUX-12043`. */
export default async function PasswordExpiredPage({
  params,
  searchParams,
}: PageProps<"/[tenant]/password-expired">) {
  const { tenant } = await params;
  const { user } = await searchParams;
  const t = await getTranslations();
  return (
    <PublicPage
      tenant={tenant}
      title={t("PasswordExpired")}
      description={t("ChangePasswordRequired")}
    >
      {() => (
        <ExpiredPasswordForm tenant={tenant} userName={typeof user === "string" ? user : ""} />
      )}
    </PublicPage>
  );
}
