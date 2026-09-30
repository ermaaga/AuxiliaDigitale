import { getTranslations } from "next-intl/server";

import { ActivationForm } from "@/features/platform/components/activation-form";
import { PlatformPublicPage } from "@/features/platform/components/platform-public-page";

/** `/platform/activate?token=…` (N02, D-22): a new or reset System user enrols the authenticator and sets the password. */
export default async function PlatformActivatePage({
  searchParams,
}: PageProps<"/platform/activate">) {
  const { token } = await searchParams;
  const t = await getTranslations();
  return (
    <PlatformPublicPage
      title={t("app.platform.activate.title")}
      description={t("app.platform.activate.description")}
    >
      <ActivationForm token={typeof token === "string" ? token : undefined} />
    </PlatformPublicPage>
  );
}
