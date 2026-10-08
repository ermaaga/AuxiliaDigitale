import { getTranslations } from "next-intl/server";

import { TwoFactorSetupForm } from "@/features/auth/components/two-factor-setup-form";

import { PublicPage } from "../public-page";

/** `/{tenant}/two-factor-setup` (N04): reached from the sign-in when the user's role requires the app (`AUX-12074`). */
export default async function TwoFactorSetupPage({
  params,
  searchParams,
}: PageProps<"/[tenant]/two-factor-setup">) {
  const { tenant } = await params;
  const { user } = await searchParams;
  const t = await getTranslations();
  return (
    <PublicPage
      tenant={tenant}
      title={t("app.twoFactor.setupTitle")}
      description={t("app.twoFactor.setupDescription")}
    >
      {(_, options) => (
        <TwoFactorSetupForm
          tenant={tenant}
          userName={typeof user === "string" ? user : ""}
          rememberMeDays={options.rememberMeDays}
        />
      )}
    </PublicPage>
  );
}
