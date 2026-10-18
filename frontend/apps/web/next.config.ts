import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// Request configuration of next-intl (language and messages of each request).
const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

/** Security headers of every response, API routes included (the CSP of pages is set per request in src/proxy.ts). */
const securityHeaders = [
  { key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains; preload" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "X-Frame-Options", value: "DENY" },
  {
    key: "Permissions-Policy",
    value: "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
  },
  { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
];

/**
 * The PDF.js worker of the document preview parses untrusted files (F14, ADR 0020): a worker follows the CSP of its own
 * script, so it may load and fetch only from the app and compile its WebAssembly decoders, nothing else.
 */
const PDFJS_WORKER_CSP = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  async headers() {
    return [
      { source: "/:path*", headers: securityHeaders },
      {
        source: "/static/pdfjs/:path*",
        headers: [{ key: "Content-Security-Policy", value: PDFJS_WORKER_CSP }],
      },
    ];
  },
};

export default withNextIntl(nextConfig);
