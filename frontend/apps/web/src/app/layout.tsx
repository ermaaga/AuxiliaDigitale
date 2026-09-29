import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

// Fonts are downloaded at build time and served by the app (self-hosted, no runtime calls to Google).
const sans = Geist({ variable: "--font-sans-family", subsets: ["latin"] });
const mono = Geist_Mono({ variable: "--font-mono-family", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Auxilia",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  // `lang` becomes the user's language when i18n is wired (task P3-05).
  return (
    <html
      lang="it"
      className={`${sans.variable} ${mono.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="flex min-h-full flex-col font-sans">{children}</body>
    </html>
  );
}
