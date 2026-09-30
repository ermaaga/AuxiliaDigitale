"use client";

import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

export const PAGE_SIZES = [10, 25, 50, 100] as const;

const paging = {
  page: parseAsInteger.withDefault(1),
  pageSize: parseAsInteger.withDefault(25),
  sort: parseAsString,
};

/**
 * State of a server-side table in the URL (skill auxilia-frontend-feature: `?page=&pageSize=&sort=&filter[x]=`), so
 * a list can be bookmarked, shared and survives reload/back. `filters` are string filters sent as `filter[name]`;
 * changing a filter, the sort or the page size goes back to page 1.
 */
export function useTableState<TFilter extends string>(filters: readonly TFilter[]) {
  const [state, setState] = useQueryStates(paging, { history: "replace" });
  const filterParsers: Record<string, typeof parseAsString> = Object.fromEntries(
    filters.map((name) => [name, parseAsString]),
  );
  const urlKeys: Record<string, string> = Object.fromEntries(
    filters.map((name) => [name, `filter[${name}]`]),
  );
  const [filterState, setFilterState] = useQueryStates(filterParsers, {
    urlKeys,
    history: "replace",
  });

  const pageSize = (PAGE_SIZES as readonly number[]).includes(state.pageSize) ? state.pageSize : 25;
  const values = Object.fromEntries(
    filters.map((name) => [name, filterState[name] || undefined]),
  ) as Record<TFilter, string | undefined>;

  return {
    page: Math.max(1, state.page),
    pageSize,
    sort: state.sort,
    filters: values,
    hasFilters: filters.some((name) => Boolean(values[name])),
    setPage: (page: number) => void setState({ page }),
    setPageSize: (size: number) => void setState({ pageSize: size, page: 1 }),
    setSort: (sort: string | null) => void setState({ sort, page: 1 }),
    setFilter: (name: TFilter, value: string | undefined) => {
      void setFilterState({ [name]: value || null });
      void setState({ page: 1 });
    },
    clearFilters: () => {
      void setFilterState(Object.fromEntries(filters.map((name) => [name, null])));
      void setState({ page: 1 });
    },
  };
}
