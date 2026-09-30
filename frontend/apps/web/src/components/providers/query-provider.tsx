"use client";

import * as React from "react";
import { QueryClientProvider } from "@tanstack/react-query";

import { createQueryClient } from "@/lib/api/query-client";

/** One QueryClient per browser tab (created once, never shared between requests on the server). */
export function QueryProvider({ children }: { children: React.ReactNode }) {
  const [client] = React.useState(() => createQueryClient());
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
