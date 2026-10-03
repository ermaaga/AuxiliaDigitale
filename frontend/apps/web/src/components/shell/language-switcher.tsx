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

/**
 * Language switch (F24): stores the choice in the `aux_lang` cookie and renders the page again in that language;
 * `onChange` is told the choice too (the tenant app saves it in the profile, F04).
 */
export function LanguageSwitcher({
  languages,
  onChange,
}: {
  languages: readonly LanguageOption[];
  onChange?: (code: string) => void | Promise<void>;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const router = useRouter();

  if (languages.length < 2) {
    return null;
  }

  async function choose(code: string) {
    setLanguageCookie(code);
    await onChange?.(code);
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
        <DropdownMenuRadioGroup value={locale} onValueChange={(code) => void choose(code)}>
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

/** The UI language of this browser (`aux_lang`, read by `i18n/request.ts`) for a year. */
export function setLanguageCookie(code: string) {
  document.cookie = `${LOCALE_COOKIE}=${encodeURIComponent(code)}; Path=/; Max-Age=31536000; SameSite=Lax`;
}
