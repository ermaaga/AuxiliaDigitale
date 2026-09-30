import type { Metadata } from "next";
import { headers } from "next/headers";
import { NextIntlClientProvider } from "next-intl";
import { getLocale } from "next-intl/server";
import { Geist, Geist_Mono } from "next/font/google";
import { ThemeProvider } from "@auxilia/ui/components/theme-provider";
import { Toaster } from "@auxilia/ui/components/sonner";
import { TooltipProvider } from "@auxilia/ui/components/tooltip";
import { NuqsAdapter } from "nuqs/adapters/next/app";
import { ConfirmProvider } from "@/components/confirm/confirm-provider";
import { QueryProvider } from "@/components/providers/query-provider";
import "./globals.css";

// Fonts are downloaded at build time and served by the app (self-hosted, no runtime calls to Google).
const sans = Geist({ variable: "--font-sans-family", subsets: ["latin"] });
const mono = Geist_Mono({ variable: "--font-mono-family", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Auxilia",
};

export default async function RootLayout({ children }: LayoutProps<"/">) {
  // Per-request CSP nonce from src/proxy.ts: pages are rendered dynamically so Next.js can stamp its scripts with it.
  const nonce = (await headers()).get("x-nonce") ?? undefined;
  const locale = await getLocale();

  // next-intl resolves the language and messages per request (src/i18n/request.ts). next-themes sets the `dark` class on <html>
  // before hydration, hence suppressHydrationWarning. Tenant layouts add <BrandingStyle> for the tenant colours.
  return (
    <html
      lang={locale}
      className={`${sans.variable} ${mono.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="flex min-h-full flex-col font-sans">
        <ThemeProvider nonce={nonce}>
          <NextIntlClientProvider>
            <QueryProvider>
              <TooltipProvider>
                <NuqsAdapter>
                  <ConfirmProvider>{children}</ConfirmProvider>
                </NuqsAdapter>
                <Toaster richColors closeButton />
              </TooltipProvider>
            </QueryProvider>
          </NextIntlClientProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
