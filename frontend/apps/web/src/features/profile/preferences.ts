"use client";

import { setLanguageCookie } from "@/components/shell/language-switcher";

import { getProfile, toThemeName } from "./api";

/**
 * Applies the language and theme saved in the profile to this browser right after sign-in (F04: the language chosen
 * in the profile holds at the next sign-in; Q35 theme). Best effort: without a profile (or with the API unreachable)
 * the browser keeps its own choices.
 */
export async function applyProfilePreferences(setTheme: (theme: string) => void): Promise<void> {
  try {
    const profile = await getProfile();
    setLanguageCookie(profile.languageCode);
    setTheme(toThemeName(profile.theme));
  } catch {
    // The sign-in succeeded: the preferences are a convenience.
  }
}
