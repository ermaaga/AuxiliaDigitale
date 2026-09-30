/** ProblemDetails produced by the BFF itself; codes of the web app are `AUX-WEB-*` (the API owns `AUX-NNNNN`). */
export function problem(status: number, title: string, errorCode: string): Response {
  return Response.json(
    { type: "about:blank", title, status, errorCode },
    {
      status,
      headers: { "content-type": "application/problem+json", "cache-control": "no-store" },
    },
  );
}

export const Problems = {
  csrf: () => problem(403, "The request was refused (CSRF protection)", "AUX-WEB-403-CSRF"),
  unauthenticated: () => problem(401, "Sign in to continue", "AUX-WEB-401"),
  forbiddenPath: () => problem(404, "Not found", "AUX-WEB-404"),
  badRequest: () => problem(400, "The request is not valid", "AUX-WEB-400"),
  apiUnavailable: () => problem(502, "The service is not reachable", "AUX-WEB-502"),
};
