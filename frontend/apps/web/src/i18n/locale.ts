/** Cookie with the language the user picked (topbar switch, profile); only a hint, validated on every request. */
export const LOCALE_COOKIE = "aux_lang";

export type LocaleInput = {
  /** Value of the language cookie. */
  preferred?: string | null;
  /** `Accept-Language` header of the browser. */
  acceptLanguage?: string | null;
  /** Active languages of the tenant (or of the static bundles), in order. */
  available: readonly string[];
  /** Tenant default language (catalog), used before the first available language. */
  fallback?: string | null;
};

/**
 * The language of a page: the user's choice if the tenant offers it, else the best browser language (by quality,
 * exact tag first, then the base language: `it-IT` → `it`), else the tenant default, else the first available one.
 */
export function resolveLocale({
  preferred,
  acceptLanguage,
  available,
  fallback,
}: LocaleInput): string {
  const offered = new Set(available);
  if (preferred && offered.has(preferred)) {
    return preferred;
  }

  for (const tag of browserLanguages(acceptLanguage)) {
    if (offered.has(tag)) {
      return tag;
    }

    const base = tag.split("-")[0]!;
    if (offered.has(base)) {
      return base;
    }
  }

  if (fallback && offered.has(fallback)) {
    return fallback;
  }

  return available[0] ?? "it";
}

function browserLanguages(header: string | null | undefined): string[] {
  if (!header) {
    return [];
  }

  return header
    .split(",")
    .map((part, index) => {
      const [tag = "", ...parameters] = part.trim().split(";");
      const quality = parameters.map((p) => p.trim()).find((p) => p.startsWith("q="));
      return { tag: normalize(tag), quality: quality ? Number(quality.slice(2)) : 1, index };
    })
    .filter((item) => item.tag !== "" && item.tag !== "*" && item.quality > 0)
    .sort((a, b) => b.quality - a.quality || a.index - b.index)
    .map((item) => item.tag);
}

/** `pt-br` → `pt-BR`, as the language codes of the API. */
function normalize(tag: string): string {
  const [language = "", region] = tag.trim().split("-");
  return region ? `${language.toLowerCase()}-${region.toUpperCase()}` : language.toLowerCase();
}
