import * as React from "react";
import { getTranslations } from "next-intl/server";

import { LanguageSwitcher } from "@/components/shell/language-switcher";
import { ThemeSwitcher } from "@/components/shell/theme-switcher";
import { AuthCard } from "@/features/auth/components/auth-card";

import { consoleLanguages } from "../languages";

/** Frame of the public pages of the console (sign-in, activation): console name, language and theme switches. */
export async function PlatformPublicPage({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  const t = await getTranslations();
  return (
    <AuthCard
      appName={t("app.platform.consoleName")}
      title={title}
      description={description}
      toolbar={
        <>
          <LanguageSwitcher languages={consoleLanguages(t)} />
          <ThemeSwitcher />
        </>
      }
    >
      {children}
    </AuthCard>
  );
}
