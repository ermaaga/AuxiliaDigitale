"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { useTheme } from "@auxilia/ui/components/theme-provider";

import { setLanguageCookie, type LanguageOption } from "@/components/shell/language-switcher";
import { useNotify } from "@/lib/notify";

import {
  changeLanguage,
  changeTheme,
  THEMES,
  toThemeName,
  useProfileMutation,
  type ApiTheme,
  type Profile,
} from "../api";

/**
 * Language and theme of the own profile (F04, Q35): the language switches the interface at once and holds at the next
 * sign-in (only the tenant's active languages); the theme applies to this browser at once and to the next sign-ins.
 */
export function ProfilePreferences({
  tenant,
  profile,
  languages,
}: {
  tenant: string;
  profile: Profile;
  languages: readonly LanguageOption[];
}) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const { setTheme } = useTheme();
  const language = useProfileMutation(tenant, changeLanguage);
  const theme = useProfileMutation(tenant, changeTheme);

  const onLanguage = async (code: string) => {
    try {
      await language.mutateAsync(code);
      setLanguageCookie(code);
      notify.success("LanguageChanged");
      router.refresh();
    } catch (error) {
      notify.error(error);
    }
  };

  const onTheme = async (value: string) => {
    try {
      const saved = await theme.mutateAsync(value as ApiTheme);
      setTheme(toThemeName(saved.theme));
      notify.success("app.profile.themeSaved");
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.profile.preferences")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent className="grid gap-4 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Label htmlFor="profile-language">{t("Language")}</Label>
          <Select
            value={profile.languageCode}
            onValueChange={(code) => void onLanguage(code)}
            disabled={language.isPending || languages.length === 0}
          >
            <SelectTrigger id="profile-language" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {languages.map((item) => (
                <SelectItem key={item.code} value={item.code} lang={item.code}>
                  {item.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="profile-theme">{t("app.profile.theme")}</Label>
          <Select
            value={profile.theme}
            onValueChange={(value) => void onTheme(value)}
            disabled={theme.isPending}
          >
            <SelectTrigger id="profile-theme" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(Object.keys(THEMES) as ApiTheme[]).map((item) => (
                <SelectItem key={item} value={item}>
                  {t(`common.theme.${THEMES[item]}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </CardContent>
    </Card>
  );
}
