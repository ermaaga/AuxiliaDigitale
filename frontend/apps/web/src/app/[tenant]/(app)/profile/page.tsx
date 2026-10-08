import { getTranslations } from "next-intl/server";

import { ProfileView } from "@/features/profile/components/profile-view";
import { tenantLanguages } from "@/i18n/bundles";

/**
 * `/{tenant}/profile` (F04, account menu "My profile"): every signed-in user of the tenant, on their own data.
 * `?twoFactor=suggest` after the temporary password was changed offers the authenticator app first (N04).
 */
export default async function ProfilePage({
  params,
  searchParams,
}: PageProps<"/[tenant]/profile">) {
  const { tenant } = await params;
  const { twoFactor } = await searchParams;
  const t = await getTranslations();
  const languages = (await tenantLanguages(tenant)) ?? [];

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("MyProfile")}</h1>
      <ProfileView
        tenant={tenant}
        languages={languages}
        suggestTwoFactor={twoFactor === "suggest"}
      />
    </div>
  );
}
