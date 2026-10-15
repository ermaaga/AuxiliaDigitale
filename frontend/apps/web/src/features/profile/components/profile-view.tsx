"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { parseAsString, parseAsStringLiteral, useQueryStates } from "nuqs";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import type { LanguageOption } from "@/components/shell/language-switcher";

import { useProfile } from "../api";
import { ProfileDataForm } from "./profile-data-form";
import { ProfilePassword } from "./profile-password";
import { ProfilePicture } from "./profile-picture";
import { ProfilePreferences } from "./profile-preferences";
import { TwoFactorCard } from "@/features/two-factor";
import { CONNECT_ACCOUNTS_PERMISSION, ConnectedAccountsPanel } from "@/features/mailbox";
import { useCan } from "@/lib/permissions";

const TABS = ["profile", "accounts"] as const;

/** An error code the BFF callback may put in the URL (`AUX-30010`, `AUX-WEB-502`); anything else is ignored. */
const ERROR_CODE = /^AUX-[A-Z0-9-]{1,32}$/;

/**
 * "My profile" (F04, legacy `/profile`, every signed-in user): picture, personal data, language and theme, password,
 * authenticator app (N04); tab `?tab=accounts` "Connected accounts" (N05) with the permission
 * `mailbox.accounts.connect` (module `mailbox` visible to the role), where the OAuth callback of the BFF comes back with
 * `connected=<id>` or `error=<code>`.
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
  const canConnect = useCan(CONNECT_ACCOUNTS_PERMISSION);
  const [params, setParams] = useQueryStates(
    {
      tab: parseAsStringLiteral(TABS).withDefault("profile"),
      connected: parseAsString,
      error: parseAsString,
    },
    { history: "replace" },
  );
  // Read once: the URL is cleaned after the panel has shown it, the alert of a failure stays.
  const [outcome] = React.useState(() => ({
    connected: params.connected ?? undefined,
    error: params.error && ERROR_CODE.test(params.error) ? params.error : undefined,
  }));
  const clearOutcome = React.useCallback(
    () => void setParams({ connected: null, error: null }),
    [setParams],
  );

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

  const profileTab = (
    <div className="flex flex-col gap-4">
      {suggestTwoFactor ? <TwoFactorCard tenant={tenant} suggest /> : null}
      <ProfilePicture tenant={tenant} profile={profile} />
      <ProfileDataForm key={profile.userId} tenant={tenant} profile={profile} />
      <ProfilePreferences tenant={tenant} profile={profile} languages={languages} />
      <ProfilePassword tenant={tenant} />
      {suggestTwoFactor ? null : <TwoFactorCard tenant={tenant} />}
    </div>
  );

  if (!canConnect) {
    return profileTab;
  }

  return (
    <Tabs
      value={params.tab}
      onValueChange={(value) => void setParams({ tab: value as (typeof TABS)[number] })}
    >
      <TabsList aria-label={t("MyProfile")} className="h-auto flex-wrap">
        <TabsTrigger value="profile">{t("app.profile.tabs.profile")}</TabsTrigger>
        <TabsTrigger value="accounts">{t("app.profile.tabs.accounts")}</TabsTrigger>
      </TabsList>
      <TabsContent value="profile" className="mt-4">
        {profileTab}
      </TabsContent>
      <TabsContent value="accounts" className="mt-4">
        <ConnectedAccountsPanel tenant={tenant} outcome={outcome} onOutcomeShown={clearOutcome} />
      </TabsContent>
    </Tabs>
  );
}
