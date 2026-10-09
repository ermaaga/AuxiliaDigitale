/**
 * The CSP of pages; `apiPublicUrl` is allowed for browser connections to the API (SignalR). `upgrade-insecure-requests`
 * only when the page itself is served over HTTPS: on plain http (local stack, E2E) Safari would upgrade
 * `http://localhost/_next/static/…` to https too (Chrome exempts localhost) and the page would load without styles.
 */
export function contentSecurityPolicy(
  nonce: string,
  development: boolean,
  apiPublicUrl?: string,
  secure = true,
): string {
  // The hub is a WebSocket: Chrome does not let an http(s) source cover ws(s), so both are listed.
  const socket = apiPublicUrl?.replace(/^http/, "ws");
  const connect = ["'self'", apiPublicUrl, socket].filter(Boolean).join(" ");
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${development ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' blob: data:",
    // FullCalendar embeds its icon font as a data: URL in its stylesheet.
    "font-src 'self' data:",
    `connect-src ${connect}`,
    // The PDF.js worker of the document preview, served by the app (/static/pdfjs); never a blob: or another host.
    "worker-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    ...(!development && secure ? ["upgrade-insecure-requests"] : []),
  ].join("; ");
}
