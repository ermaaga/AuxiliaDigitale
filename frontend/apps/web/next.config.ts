import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  // Security headers, CSP with nonce and the BFF are added in task P3-03.
};

export default nextConfig;
