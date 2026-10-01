"use client";

import { useQuery } from "@tanstack/react-query";
import { unwrap } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

import type { GridLayoutColumn } from "./table-model";

/**
 * The column layout of a grid for the signed-in user (F21, `GET /me/grids/{key}`), ready for `applyLayout`. A grid the
 * user's roles do not see, or an API error, leaves the columns as the page declares them.
 */
export function useGridLayout(tenant: string, gridKey: string) {
  const query = useQuery({
    queryKey: queryKey(tenant, "configuration", "grid-layout", { gridKey }),
    queryFn: async () =>
      unwrap(
        await createBffClient("tenant").GET("/api/v1/me/grids/{key}", {
          params: { path: { key: gridKey } },
        }),
      ),
    retry: false,
    staleTime: 60_000,
  });

  const layout: GridLayoutColumn[] | undefined = query.data?.columns.map((column, order) => ({
    key: column.key,
    visible: column.visible,
    order,
  }));
  return { layout, isPending: query.isPending };
}
