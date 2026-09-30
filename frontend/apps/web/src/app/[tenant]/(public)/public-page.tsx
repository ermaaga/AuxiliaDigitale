import * as React from "react";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { LanguageSwitcher } from "@/components/shell/language-switcher";
import { ThemeSwitcher } from "@/components/shell/theme-switcher";
import { AuthCard } from "@/features/auth/components/auth-card";
import { tenantLanguages } from "@/i18n/bundles";
import { publicApi } from "@/lib/api/server";
import { DEFAULT_APP_NAME } from "@/lib/app";

/**
 * Frame of every public page of a tenant: 404 when the tenant does not exist, the tenant status message when it is
 * suspended or unavailable, language and theme switches in the corner.
 */
export async function PublicPage({
  tenant,
  title,
  description,
  children,
}: {
  tenant: string;
  title: string;
  description?: string;
  children: (methods: readonly string[]) => React.ReactNode;
}) {
  const t = await getTranslations();
  let methods: readonly string[] = ["password"];
  let problem: string | undefined;
  let response: Response | undefined;
  try {
    response = await publicApi(tenant, "auth/methods");
  } catch {
    problem = t("errors.AUX-WEB-502");
  }

  if (response?.status === 404) {
    notFound();
  }

  if (response?.ok) {
    methods = ((await response.json()) as { methods: string[] }).methods;
  } else if (response) {
    const code = ((await response.json().catch(() => ({}))) as { errorCode?: string }).errorCode;
    problem = code && t.has(`errors.${code}`) ? t(`errors.${code}`) : t("errors.generic");
  }

  const languages = (await tenantLanguages(tenant)) ?? [];
  const toolbar = (
    <>
      <LanguageSwitcher languages={languages} />
      <ThemeSwitcher />
    </>
  );

  return (
    <AuthCard appName={DEFAULT_APP_NAME} title={title} description={description} toolbar={toolbar}>
      {problem ? (
        <p role="alert" className="text-sm text-destructive">
          {problem}
        </p>
      ) : (
        children(methods)
      )}
    </AuthCard>
  );
}
