import { cookies, headers } from "next/headers";
import { getRequestConfig } from "next-intl/server";

import { tenantBundle, tenantLanguages } from "./bundles";
import { LOCALE_COOKIE, resolveLocale } from "./locale";
import { FALLBACK_LOCALES, fallbackMessages, unflatten } from "./messages";
import { TENANT_HEADER } from "./tenant";

/**
 * Language and messages of a request (next-intl without locale routing: the tenant is the first path segment,
 * ARCHITECTURE §14). Tenant pages use the tenant's languages and its bundle from the API (edits of the System show up
 * within seconds); without a tenant (console) or when the API cannot answer, the static bundles generated from the
 * seed keep every page, the login included, readable.
 */
export default getRequestConfig(async () => {
  const requestHeaders = await headers();
  const tenant = requestHeaders.get(TENANT_HEADER) ?? undefined;
  const offered = tenant ? await tenantLanguages(tenant) : undefined;

  const locale = resolveLocale({
    preferred: (await cookies()).get(LOCALE_COOKIE)?.value,
    acceptLanguage: requestHeaders.get("accept-language"),
    available: offered?.map((language) => language.code) ?? FALLBACK_LOCALES,
    fallback: offered?.find((language) => language.isDefault)?.code ?? "it",
  });

  const flat =
    (tenant ? await tenantBundle(tenant, locale) : undefined) ?? fallbackMessages(locale);

  return {
    locale,
    messages: unflatten(flat),
    // Dates are shown in the tenant's time zone (skill auxilia-localization); until the tenant settings reach the
    // web app (S-02) the platform default applies.
    timeZone: "Europe/Rome",
    // A missing key shows the key itself (F24 fallback chain ends with the key) and is not an error in production.
    getMessageFallback: ({ key, namespace }) => (namespace ? `${namespace}.${key}` : key),
    onError: (error) => {
      if (process.env.NODE_ENV === "development") {
        console.warn(`[i18n] ${error.message}`);
      }
    },
  };
});
