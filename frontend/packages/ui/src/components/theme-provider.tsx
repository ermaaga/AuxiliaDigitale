"use client";

import * as React from "react";
import { ThemeProvider as NextThemesProvider, useTheme } from "next-themes";

/**
 * Light / dark / system theme (next-themes, `class` strategy on <html>; `suppressHydrationWarning` is required there).
 * The choice is kept in the browser; the tenant app seeds it from the profile preference at sign-in (B-05, Q35).
 */
function ThemeProvider({ children, ...props }: React.ComponentProps<typeof NextThemesProvider>) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
      {...props}
    >
      {children}
    </NextThemesProvider>
  );
}

export { ThemeProvider, useTheme };
