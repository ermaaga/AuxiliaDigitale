import createClient, { type ClientOptions } from "openapi-fetch";
import type { paths } from "./schema";

export type { paths, components } from "./schema";
export * from "./errors";

/** Typed API client. Browser code calls the BFF (`/api/bff`); server code calls the API through `lib/api/server`. */
export function createApiClient(options: ClientOptions) {
  return createClient<paths>(options);
}

export type ApiClient = ReturnType<typeof createApiClient>;
