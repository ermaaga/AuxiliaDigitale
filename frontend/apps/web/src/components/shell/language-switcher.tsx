"use client";

import { useRouter } from "next/navigation";
import { LanguagesIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";

import { LOCALE_COOKIE } from "@/i18n/locale";

export type LanguageOption = { code: string; name: string };

/** Language switch (F24): stores the choice in the `aux_lang` cookie and renders the page again in that language. */
export function LanguageSwitcher({ languages }: { languages: readonly LanguageOption[] }) {
  const t = useTranslations();
  const locale = useLocale();
  const router = useRouter();

  if (languages.length < 2) {
    return null;
  }

  function choose(code: string) {
    document.cookie = `${LOCALE_COOKIE}=${encodeURIComponent(code)}; Path=/; Max-Age=31536000; SameSite=Lax`;
    router.refresh();
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("Language")}>
          <LanguagesIcon aria-hidden />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t("Language")}</DropdownMenuLabel>
        <DropdownMenuRadioGroup value={locale} onValueChange={choose}>
          {languages.map((language) => (
            <DropdownMenuRadioItem key={language.code} value={language.code} lang={language.code}>
              {language.name}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
