import { getTranslations } from "next-intl/server";

import { ProfileView } from "@/features/profile/components/profile-view";
import { tenantLanguages } from "@/i18n/bundles";

/** `/{tenant}/profile` (F04, account menu "My profile"): every signed-in user of the tenant, on their own data. */
export default async function ProfilePage({ params }: PageProps<"/[tenant]/profile">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const languages = (await tenantLanguages(tenant)) ?? [];

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("MyProfile")}</h1>
      <ProfileView tenant={tenant} languages={languages} />
    </div>
  );
}
