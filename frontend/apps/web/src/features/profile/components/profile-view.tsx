"use client";

import { useTranslations } from "next-intl";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import type { LanguageOption } from "@/components/shell/language-switcher";

import { useProfile } from "../api";
import { ProfileDataForm } from "./profile-data-form";
import { ProfilePassword } from "./profile-password";
import { ProfilePicture } from "./profile-picture";
import { ProfilePreferences } from "./profile-preferences";
import { TwoFactorCard } from "@/features/two-factor";

/**
 * "My profile" (F04, legacy `/profile`, every signed-in user): picture, personal data, language and theme, password,
 * authenticator app (N04).
 * The own sessions get their page with B-21.
 */
export function ProfileView({
  tenant,
  languages,
  suggestTwoFactor = false,
}: {
  tenant: string;
  languages: readonly LanguageOption[];
  /** First visit after the temporary password was changed: the authenticator app is offered first (N04). */
  suggestTwoFactor?: boolean;
}) {
  const t = useTranslations();
  const query = useProfile(tenant);

  if (query.error) {
    return <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />;
  }

  const profile = query.data;
  if (!profile) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true" aria-label={t("MyProfile")}>
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {suggestTwoFactor ? <TwoFactorCard tenant={tenant} suggest /> : null}
      <ProfilePicture tenant={tenant} profile={profile} />
      <ProfileDataForm key={profile.userId} tenant={tenant} profile={profile} />
      <ProfilePreferences tenant={tenant} profile={profile} languages={languages} />
      <ProfilePassword tenant={tenant} />
      {suggestTwoFactor ? null : <TwoFactorCard tenant={tenant} />}
    </div>
  );
}
