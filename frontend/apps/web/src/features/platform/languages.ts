import { FALLBACK_LOCALES } from "@/i18n/messages";

/** Legacy keys of the language names offered by the console (it has no tenant: the languages of the static bundles). */
const LANGUAGE_NAME_KEYS: Record<string, string> = { en: "English", it: "Italian" };

/** Languages of the console's language switch, named in the current language. */
export function consoleLanguages(t: (key: string) => string) {
  return FALLBACK_LOCALES.map((code) => ({
    code,
    name: LANGUAGE_NAME_KEYS[code] ? t(LANGUAGE_NAME_KEYS[code]) : code,
  }));
}
