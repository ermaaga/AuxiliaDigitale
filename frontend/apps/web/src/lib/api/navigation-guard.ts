import { cache } from "react";
import { notFound } from "next/navigation";

import { serverApi } from "./server";

/** The navigation keys of the signed-in user (`/me/navigation`), once per request; `undefined` when it cannot be read. */
const navigationKeys = cache(async (): Promise<ReadonlySet<string> | undefined> => {
  const response = await serverApi("tenant", "me/navigation");
  if (!response.ok) {
    // 401 and errors are handled by the `(app)` layout (sign-in page or error boundary).
    return undefined;
  }

  return new Set(((await response.json()) as Array<{ key: string }>).map((item) => item.key));
});

/**
 * A section of the tenant app opens only for the users whose menu has it (F22, H-04): the pages of other roles answer
 * the 404 page, like a hidden module, instead of a page whose every call is refused. UX only: the API still decides.
 */
export async function requireNavigation(key: string): Promise<void> {
  const keys = await navigationKeys();
  if (keys && !keys.has(key)) {
    notFound();
  }
}
