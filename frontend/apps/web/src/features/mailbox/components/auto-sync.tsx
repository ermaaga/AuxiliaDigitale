"use client";

import * as React from "react";
import { LoaderCircleIcon } from "lucide-react";

import { useNotify } from "@/lib/notify";
import { useRealtimeConnected } from "@/lib/realtime/realtime";

import { isSyncActive, useStartSync, useSyncStatus, type SyncScope } from "../api";

/**
 * The on-demand sync of a page (N05 #106): opening the page asks for a sync of the scope once (a recent or running run
 * is returned by the API instead), the status is polled only without the realtime hub (`AccountSyncCompleted` refreshes
 * it otherwise), a short toast (`completedKey`) when the runs seen running on this page are over, "Sync now".
 */
export function useAutoSync(
  tenant: string,
  scope: SyncScope,
  options: { enabled: boolean; syncable: boolean; completedKey: string },
) {
  const notify = useNotify();
  const connected = useRealtimeConnected();
  const { enabled, syncable, completedKey } = options;
  const status = useSyncStatus(tenant, scope, enabled && syncable, !connected);
  const start = useStartSync(tenant, scope);
  const runs = status.data ?? [];
  const active = runs.some(isSyncActive);
  const succeeded = runs.some((run) => run.status === "Succeeded");
  const opened = React.useRef(false);
  const wasActive = React.useRef(false);

  React.useEffect(() => {
    if (!enabled || !syncable || opened.current) {
      return;
    }

    opened.current = true;
    start.mutate(false);
  }, [enabled, syncable, start]);

  React.useEffect(() => {
    if (wasActive.current && !active && succeeded) {
      notify.success(completedKey);
    }

    wasActive.current = active;
  }, [active, succeeded, notify, completedKey]);

  return {
    runs,
    active,
    failed: runs.filter((run) => run.status === "Failed" && run.errorCode),
    syncing: active || start.isPending,
    syncNow: () => start.mutate(true, { onError: (error) => notify.error(error) }),
  };
}

/** The "Synchronising…" line, announced politely; empty (but present for the live region) when idle. */
export function SyncProgress({ active, label }: { active: boolean; label: string }) {
  return (
    <div role="status" aria-live="polite" className="text-sm">
      {active ? (
        <p className="flex items-center gap-2 rounded-md border bg-muted px-3 py-2 text-muted-foreground">
          <LoaderCircleIcon
            aria-hidden
            className="size-4 animate-spin motion-reduce:animate-none"
          />
          {label}
        </p>
      ) : null}
    </div>
  );
}
