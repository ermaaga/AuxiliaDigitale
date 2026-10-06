/**
 * Optimistic concurrency of shared resources in the browser (F29). The API answers a resource's GET with an `ETag`
 * and refuses a write of it without `If-Match` (428) or with an outdated one (412, "modified by someone else"). The BFF
 * client remembers the ETag of every GET and sends it back on the writes of the routes below; a write from a page that
 * never read the resource (a delete from a list) reads it first. The list is checked against the OpenAPI contract
 * (every operation documenting 428) by a test.
 */
export const VERSIONED_WRITES = [
  "PUT /api/v1/platform/tenants/{slug}",
  "PUT /api/v1/localization/keys/{id}",
  "DELETE /api/v1/localization/keys/{id}",
  "PUT /api/v1/clients/{id}",
  "DELETE /api/v1/clients/{id}",
  "PUT /api/v1/clients/{id}/tags",
  "PUT /api/v1/employees/{id}",
  "DELETE /api/v1/employees/{id}",
  "PUT /api/v1/services/{id}",
  "DELETE /api/v1/services/{id}",
  "PUT /api/v1/cases/{id}",
  "DELETE /api/v1/cases/{id}",
  "PUT /api/v1/documents/{id}",
  "DELETE /api/v1/documents/{id}",
  "PUT /api/v1/appointments/{id}",
  "DELETE /api/v1/appointments/{id}",
  "DELETE /api/v1/requests/{id}",
  "PUT /api/v1/tasks/{id}",
  "DELETE /api/v1/tasks/{id}",
  "PUT /api/v1/marketing/segments/{id}",
  "DELETE /api/v1/marketing/segments/{id}",
  "PUT /api/v1/marketing/lists/{id}",
  "DELETE /api/v1/marketing/lists/{id}",
  "PUT /api/v1/marketing/templates/{id}",
  "DELETE /api/v1/marketing/templates/{id}",
  "PUT /api/v1/marketing/campaigns/{id}",
  "DELETE /api/v1/marketing/campaigns/{id}",
] as const;

const MATCHERS = VERSIONED_WRITES.map((entry) => {
  const [method, template] = entry.split(" ") as [string, string];
  const pattern = template.replace(/\{[^}]+\}/g, "[^/]+");
  return { method, regex: new RegExp(`^${pattern}$`) };
});

/** Whether a write of this API path needs the version the caller read. */
export function isVersionedWrite(method: string, apiPath: string): boolean {
  const upper = method.toUpperCase();
  return MATCHERS.some((matcher) => matcher.method === upper && matcher.regex.test(apiPath));
}

/** The last ETag read per area, tenant and API path, for the lifetime of the tab. */
const versions = new Map<string, string>();

export const versionKey = (scope: string, apiPath: string) => `${scope}|${apiPath}`;

export const resourceVersions = {
  get: (key: string) => versions.get(key),
  set: (key: string, etag: string) => void versions.set(key, etag),
  /** After a write (or a 412) the version is unknown until the resource is read again. */
  forget: (key: string) => void versions.delete(key),
  clear: () => versions.clear(),
};
