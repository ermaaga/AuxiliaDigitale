"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type GridView = components["schemas"]["GridViewResponse"];
export type SaveGridView = components["schemas"]["SaveGridViewRequest"];

/** The personal views of the signed-in user on a grid (F21, `/me/grids/{key}/views`). */
export const gridViewsKey = (tenant: string, gridKey: string) =>
  queryKey(tenant, "configuration", "grid-views", { gridKey });

const client = () => createBffClient("tenant");

export function useGridViews(tenant: string, gridKey: string) {
  return useQuery({
    queryKey: gridViewsKey(tenant, gridKey),
    queryFn: async () =>
      unwrap(
        await client().GET("/api/v1/me/grids/{key}/views", { params: { path: { key: gridKey } } }),
      ),
    // A grid the user's roles do not see answers 404: the list simply has no views.
    retry: false,
    staleTime: 60_000,
  });
}

/** Create (no id), change or delete a view; every change refreshes the views of the grid. */
export function useSaveGridView(tenant: string, gridKey: string) {
  const queries = useQueryClient();
  const invalidate = () => queries.invalidateQueries({ queryKey: gridViewsKey(tenant, gridKey) });
  const path = { key: gridKey };

  const save = useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveGridView }) =>
      id
        ? unwrap(
            await client().PUT("/api/v1/me/grids/{key}/views/{id}", {
              params: { path: { ...path, id } },
              body,
            }),
          )
        : unwrap(await client().POST("/api/v1/me/grids/{key}/views", { params: { path }, body })),
    onSettled: invalidate,
  });

  const remove = useMutation({
    mutationFn: async (id: string) =>
      unwrap(
        await client().DELETE("/api/v1/me/grids/{key}/views/{id}", {
          params: { path: { ...path, id } },
        }),
      ),
    onSettled: invalidate,
  });

  return { save, remove };
}

/** The request that saves the current state of a list under `name`. */
export function currentView(
  name: string,
  hiddenColumns: readonly string[],
  filters: Record<string, string | undefined>,
  sort: string | null,
  isDefault: boolean,
): SaveGridView {
  return {
    name: name.trim(),
    hiddenColumns: [...hiddenColumns],
    filters: Object.fromEntries(
      Object.entries(filters).filter((entry): entry is [string, string] => Boolean(entry[1])),
    ),
    sort,
    isDefault,
  };
}
