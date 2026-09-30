import type { Metadata } from "next";
import { headers } from "next/headers";
import { Geist, Geist_Mono } from "next/font/google";
import { ThemeProvider } from "@auxilia/ui/components/theme-provider";
import { Toaster } from "@auxilia/ui/components/sonner";
import { TooltipProvider } from "@auxilia/ui/components/tooltip";
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

  // `lang` becomes the user's language when i18n is wired (task P3-05). next-themes sets the `dark` class on <html>
  // before hydration, hence suppressHydrationWarning. Tenant layouts add <BrandingStyle> for the tenant colours.
  return (
    <html
      lang="it"
      className={`${sans.variable} ${mono.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="flex min-h-full flex-col font-sans">
        <ThemeProvider nonce={nonce}>
          <QueryProvider>
            <TooltipProvider>
              {children}
              <Toaster richColors closeButton />
            </TooltipProvider>
          </QueryProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
