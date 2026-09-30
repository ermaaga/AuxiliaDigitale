import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query";
import { isApiError } from "@auxilia/api-client";

/** Event dispatched on `window` when an API call answers 401: the shell signs out and goes to the login page. */
export const UNAUTHENTICATED_EVENT = "auxilia:unauthenticated";

/** Queries retry transient failures (network, 408, 429, 5xx) twice; everything else fails at once. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  return isApiError(error) && error.isTransient && failureCount < 2;
}

/**
 * The React Query client of the app (skill auxilia-frontend-feature): short freshness for reference data, no retry of
 * mutations (they are not all idempotent), and one place that reacts to an ended session.
 */
export function createQueryClient(
  onUnauthenticated: () => void = dispatchUnauthenticated,
): QueryClient {
  const onError = (error: unknown) => {
    if (isApiError(error) && error.isUnauthenticated) {
      onUnauthenticated();
    }
  };

  return new QueryClient({
    queryCache: new QueryCache({ onError }),
    mutationCache: new MutationCache({ onError }),
    defaultOptions: {
      queries: { staleTime: 30_000, retry: shouldRetry, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
}

function dispatchUnauthenticated() {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new Event(UNAUTHENTICATED_EVENT));
  }
}
