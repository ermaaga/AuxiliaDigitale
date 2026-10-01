/** Translator as both next-intl variants (client and server) expose it. */
export type Translate = {
  (key: string, values?: Record<string, string | number>): string;
  has(key: string): boolean;
};

const translated = (t: Translate, key: string, fallback: string) =>
  t.has(key) ? t(key) : fallback;

/** Tenant role (`Administrator`, `Employee`, `Client`) with the legacy role names. */
export const roleLabel = (t: Translate, role: string) => translated(t, role, role);

export const runKindLabel = (t: Translate, kind: string) =>
  translated(t, `app.platform.runKind.${kind}`, kind);

export const runStatusLabel = (t: Translate, status: string) =>
  translated(t, `app.platform.runStatus.${status}`, status);

/** Module or plan name from its translation key (the key itself when untranslated). */
export const nameOf = (t: Translate, nameKey: string, code: string) => translated(t, nameKey, code);

/** Roles of a module as text, `Nobody` when empty. */
export function rolesText(t: Translate, roles: readonly string[]): string {
  return roles.length === 0
    ? t("app.platform.modules.none")
    : roles.map((role) => roleLabel(t, role)).join(", ");
}

/** IANA time zones of the browser's Intl data (the API checks them again). */
export function timeZones(): string[] {
  try {
    return Intl.supportedValuesOf("timeZone");
  } catch {
    return ["Europe/Rome"];
  }
}

export const TENANT_ROLES = ["Administrator", "Employee", "Client"] as const;
