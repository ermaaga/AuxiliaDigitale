/** The CSP of pages; `apiPublicUrl` is allowed for browser connections to the API (SignalR). */
export function contentSecurityPolicy(
  nonce: string,
  development: boolean,
  apiPublicUrl?: string,
): string {
  // The hub is a WebSocket: Chrome does not let an http(s) source cover ws(s), so both are listed.
  const socket = apiPublicUrl?.replace(/^http/, "ws");
  const connect = ["'self'", apiPublicUrl, socket].filter(Boolean).join(" ");
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${development ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' blob: data:",
    "font-src 'self'",
    `connect-src ${connect}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    ...(development ? [] : ["upgrade-insecure-requests"]),
  ].join("; ");
}
