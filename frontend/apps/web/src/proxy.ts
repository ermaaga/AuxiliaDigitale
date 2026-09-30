import { NextResponse, type NextRequest } from "next/server";

import { contentSecurityPolicy } from "@/lib/security/csp";

/**
 * Per-request CSP nonce for pages (skill auxilia-security; Next.js 16 "proxy", formerly middleware). Not a security
 * boundary (CVE-2025-29927): authentication and permissions are enforced by the API. Scripts need the nonce
 * (`'strict-dynamic'`, no `unsafe-inline`; `unsafe-eval` only in development for React's debugging); styles allow
 * inline because Radix positions popovers with style attributes and sonner injects its styles at runtime.
 */
export function proxy(request: NextRequest) {
  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const csp = contentSecurityPolicy(
    nonce,
    process.env.NODE_ENV === "development",
    process.env.AUXILIA_API_PUBLIC_URL,
  );

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("content-security-policy", csp);

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set("content-security-policy", csp);
  return response;
}

export const config = {
  matcher: [
    {
      source: "/((?!api|_next/static|_next/image|favicon.ico).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
