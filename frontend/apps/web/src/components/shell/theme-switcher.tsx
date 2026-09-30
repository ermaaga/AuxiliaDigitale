"use client";

import { useTranslations } from "next-intl";
import { ThemeToggle } from "@auxilia/ui/components/theme-toggle";

/** The design-system theme toggle with its translated labels (F34: light/dark kept in this browser). */
export function ThemeSwitcher() {
  const t = useTranslations("common.theme");
  return (
    <ThemeToggle
      labels={{ toggle: t("toggle"), light: t("light"), dark: t("dark"), system: t("system") }}
    />
  );
}
