"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";

import { postToBff } from "@/lib/api/browser";
import { queryKey } from "@/lib/api/query-keys";
import { tenantHref } from "@/lib/href";

/** Events of the hub (`Contracts.Realtime.RealtimeEvents`). */
export const REALTIME_EVENTS = [
  "NotificationReceived",
  "AppointmentChanged",
  "RequestChanged",
  "DocumentProcessed",
  "RegistrationRequested",
] as const;
export type RealtimeEvent = (typeof REALTIME_EVENTS)[number];

type RealtimeToken = { accessToken: string; expiresAt: string; hubUrl: string };
type Listener = (payload: unknown) => void;

type Realtime = {
  /** Connected to the hub: pages may stop polling. */
  connected: boolean;
  subscribe: (event: RealtimeEvent, listener: Listener) => () => void;
};

const RealtimeContext = React.createContext<Realtime>({
  connected: false,
  subscribe: () => () => {},
});

/** The query prefixes each event makes stale (React Query refetches what is on screen). */
export function staleKeys(tenant: string, event: RealtimeEvent) {
  switch (event) {
    case "NotificationReceived":
      return [queryKey(tenant, "engagement", "notifications")];
    case "AppointmentChanged":
      return [queryKey(tenant, "scheduling", "appointments")];
    case "RequestChanged":
      return [queryKey(tenant, "engagement", "requests")];
    case "DocumentProcessed":
      return [queryKey(tenant, "documents")];
    case "RegistrationRequested":
      return [queryKey(tenant, "directory", "registrations")];
  }
}

/**
 * One SignalR connection per signed-in tab (skill auxilia-frontend-feature "Realtime", F16/F17): a short-lived token
 * from the BFF (`POST /api/realtime/token`, renewed at every reconnect — the hub closes the connection when the token
 * expires), WebSockets straight to the API hub, automatic reconnect. Events refresh the matching queries and reach
 * `useRealtimeEvent` listeners; `ForceLogout` signs this tab out. Without a configured hub (204) nothing connects
 * and the pages keep polling.
 */
export function RealtimeProvider({
  tenant,
  children,
}: {
  tenant: string;
  children: React.ReactNode;
}) {
  const router = useRouter();
  const client = useQueryClient();
  const [connected, setConnected] = React.useState(false);
  const listeners = React.useRef(new Map<RealtimeEvent, Set<Listener>>());

  React.useEffect(() => {
    let stopped = false;
    let connection: import("@microsoft/signalr").HubConnection | undefined;

    const token = async (): Promise<RealtimeToken | undefined> => {
      try {
        return await postToBff<RealtimeToken>("/api/realtime/token");
      } catch {
        return undefined;
      }
    };

    void (async () => {
      const first = await token();
      if (!first || stopped) {
        return;
      }

      const signalr = await import("@microsoft/signalr");
      connection = new signalr.HubConnectionBuilder()
        .withUrl(first.hubUrl, {
          // WebSockets only, no negotiate request: no cross-origin fetch to the API.
          transport: signalr.HttpTransportType.WebSockets,
          skipNegotiation: true,
          accessTokenFactory: async () => (await token())?.accessToken ?? "",
        })
        .withAutomaticReconnect()
        .configureLogging(signalr.LogLevel.None)
        .build();

      for (const event of REALTIME_EVENTS) {
        connection.on(event, (payload: unknown) => {
          for (const key of staleKeys(tenant, event)) {
            void client.invalidateQueries({ queryKey: key });
          }

          listeners.current.get(event)?.forEach((listener) => listener(payload));
        });
      }

      connection.on("ForceLogout", () => {
        void postToBff("/api/auth/logout")
          .catch(() => undefined)
          .finally(() => {
            // The same path as a sign-out: also this tab's own logout gets the push.
            router.replace(tenantHref(tenant, "/login"));
            router.refresh();
          });
      });
      connection.onreconnecting(() => setConnected(false));
      connection.onreconnected(() => setConnected(true));
      connection.onclose(() => setConnected(false));

      try {
        await connection.start();
        if (!stopped) {
          setConnected(true);
        }
      } catch {
        setConnected(false);
      }
    })();

    return () => {
      stopped = true;
      void connection?.stop();
    };
  }, [tenant, client, router]);

  const subscribe = React.useCallback((event: RealtimeEvent, listener: Listener) => {
    const set = listeners.current.get(event) ?? new Set<Listener>();
    set.add(listener);
    listeners.current.set(event, set);
    return () => {
      set.delete(listener);
    };
  }, []);

  const value = React.useMemo(() => ({ connected, subscribe }), [connected, subscribe]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

/** Whether the hub is connected (pages poll only when it is not). */
export function useRealtimeConnected(): boolean {
  return React.useContext(RealtimeContext).connected;
}

/** Runs `listener` for each `event` pushed by the hub while the component is mounted. */
export function useRealtimeEvent(event: RealtimeEvent, listener: Listener) {
  const { subscribe } = React.useContext(RealtimeContext);
  const latest = React.useRef(listener);
  React.useEffect(() => {
    latest.current = listener;
  });
  React.useEffect(() => subscribe(event, (payload) => latest.current(payload)), [subscribe, event]);
}
