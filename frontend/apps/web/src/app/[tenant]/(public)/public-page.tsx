import * as React from "react";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";

import { LanguageSwitcher } from "@/components/shell/language-switcher";
import { ThemeSwitcher } from "@/components/shell/theme-switcher";
import { AuthCard } from "@/features/auth/components/auth-card";
import { brandIdentity, loginBackground } from "@/features/branding/branding";
import { loadBranding } from "@/features/branding/server";
import { tenantLanguages } from "@/i18n/bundles";
import { publicApi } from "@/lib/api/server";

/** Sign-in options of the tenant (`GET /auth/methods`): days of "stay signed in", 0 = the box is hidden (N04). */
export type SignInOptions = { rememberMeDays: number };

/**
 * Frame of every public page of a tenant: 404 when the tenant does not exist, the tenant status message when it is
 * suspended or unavailable, the tenant branding (F23), language and theme switches in the corner.
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
  children: (methods: readonly string[], options: SignInOptions) => React.ReactNode;
}) {
  const t = await getTranslations();
  let methods: readonly string[] = ["password"];
  let options: SignInOptions = { rememberMeDays: 0 };
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
    const body = (await response.json()) as { methods: string[]; rememberMeDays?: number };
    methods = body.methods;
    options = { rememberMeDays: body.rememberMeDays ?? 0 };
  } else if (response) {
    const code = ((await response.json().catch(() => ({}))) as { errorCode?: string }).errorCode;
    problem = code && t.has(`errors.${code}`) ? t(`errors.${code}`) : t("errors.generic");
  }

  const [languages, branding] = await Promise.all([
    tenantLanguages(tenant).then((list) => list ?? []),
    loadBranding(tenant),
  ]);
  const identity = brandIdentity(tenant, branding);
  const toolbar = (
    <>
      <LanguageSwitcher languages={languages} />
      <ThemeSwitcher />
    </>
  );

  return (
    <AuthCard
      appName={identity.appName}
      logoUrl={identity.logoUrl}
      background={loginBackground(tenant, branding.background)}
      title={title}
      description={description}
      toolbar={toolbar}
    >
      {problem ? (
        <p role="alert" className="text-sm text-destructive">
          {problem}
        </p>
      ) : (
        children(methods, options)
      )}
    </AuthCard>
  );
}
