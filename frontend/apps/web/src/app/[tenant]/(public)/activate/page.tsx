import { getTranslations } from "next-intl/server";

import { SetPasswordForm } from "@/features/auth/components/set-password-form";

import { PublicPage } from "../public-page";

/** `/{tenant}/activate?token=…` (link of the activation e-mail, D-06): the user chooses the first password. */
export default async function ActivatePage({
  params,
  searchParams,
}: PageProps<"/[tenant]/activate">) {
  const { tenant } = await params;
  const { token } = await searchParams;
  const t = await getTranslations();
  return (
    <PublicPage
      tenant={tenant}
      title={t("app.auth.activate.title")}
      description={t("app.auth.activate.description")}
    >
      {() => (
        <SetPasswordForm
          tenant={tenant}
          token={typeof token === "string" ? token : undefined}
          kind="activate"
        />
      )}
    </PublicPage>
  );
}
