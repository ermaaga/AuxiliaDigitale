import type * as React from "react";

/**
 * A column of a server-side DataTable (skill auxilia-frontend-feature, F21): the header text (already translated), how
 * a row is rendered, the API sort field (sortable only when set), and how it shows on small screens (cards).
 */
export type DataTableColumn<TRow> = {
  id: string;
  header: string;
  cell: (row: TRow) => React.ReactNode;
  /** API field for `?sort=` (`attemptedAt` → `attemptedAt` / `-attemptedAt`). */
  sortField?: string;
  /** Can be hidden from the column picker (default true). */
  hideable?: boolean;
  /** Card layout below `md`: `title` rows head the card, `detail` rows are label/value pairs. Default `detail`. */
  mobile?: "title" | "detail" | "hidden";
};

/** Grid layout of a role (F21, `configuration.grid_layouts`, S-04): visible columns in their order. */
export type GridLayoutColumn = { key: string; visible: boolean; order: number };

/** A custom field definition shown in grids (F20, S-04). */
export type CustomFieldDefinition = {
  key: string;
  label: string;
  visibleOnGrid: boolean;
  order: number;
};

/** Sort cycle of a header: none → ascending → descending → none (API default order). */
export function nextSort(current: string | null | undefined, field: string): string | null {
  if (current === field) {
    return `-${field}`;
  }

  return current === `-${field}` ? null : field;
}

export function sortDirection(
  sort: string | null | undefined,
  field: string,
): "asc" | "desc" | undefined {
  if (sort === field) {
    return "asc";
  }

  return sort === `-${field}` ? "desc" : undefined;
}

export function pageCount(totalCount: number, pageSize: number): number {
  return Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)));
}

/**
 * Columns in the order and visibility of a grid layout; columns the layout does not know keep their place after the
 * known ones (a new column appears until the System configures it). Without a layout, everything as declared.
 */
export function applyLayout<TRow>(
  columns: readonly DataTableColumn<TRow>[],
  layout: readonly GridLayoutColumn[] | undefined,
): { columns: DataTableColumn<TRow>[]; hidden: string[] } {
  if (!layout || layout.length === 0) {
    return { columns: [...columns], hidden: [] };
  }

  const byKey = new Map(layout.map((item) => [item.key, item]));
  const ordered = [...columns].sort((a, b) => {
    const left = byKey.get(a.id)?.order ?? Number.MAX_SAFE_INTEGER;
    const right = byKey.get(b.id)?.order ?? Number.MAX_SAFE_INTEGER;
    return left - right;
  });
  const hidden = ordered
    .filter((column) => byKey.get(column.id)?.visible === false)
    .map((column) => column.id);
  return { columns: ordered, hidden };
}

/** Grid columns of the custom fields marked "visible on grid" (F20), after the standard ones, by their order. */
export function customFieldColumns<TRow>(
  definitions: readonly CustomFieldDefinition[],
  valueOf: (row: TRow, key: string) => React.ReactNode,
): DataTableColumn<TRow>[] {
  return [...definitions]
    .filter((definition) => definition.visibleOnGrid)
    .sort((a, b) => a.order - b.order)
    .map((definition) => ({
      id: `cf:${definition.key}`,
      header: definition.label,
      cell: (row: TRow) => valueOf(row, definition.key),
    }));
}

/**
 * Sort and page in the browser a short list the API returns whole (e.g. the tenants of the console), with the same
 * `sort` syntax as the API (`field` / `-field`); `sortValue` gives the text a row is sorted by. A page past the end
 * (after filtering) shows the last one.
 */
export function pageLocally<TRow>(
  rows: readonly TRow[],
  {
    page,
    pageSize,
    sort,
    sortValue,
  }: {
    page: number;
    pageSize: number;
    sort: string | null | undefined;
    sortValue: (row: TRow, field: string) => string;
  },
): { rows: TRow[]; totalCount: number; page: number } {
  const sorted = [...rows];
  if (sort) {
    const descending = sort.startsWith("-");
    const field = descending ? sort.slice(1) : sort;
    const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: "base" });
    sorted.sort(
      (a, b) => collator.compare(sortValue(a, field), sortValue(b, field)) * (descending ? -1 : 1),
    );
  }

  const shown = Math.min(Math.max(1, page), pageCount(sorted.length, pageSize));
  const start = (shown - 1) * pageSize;
  return { rows: sorted.slice(start, start + pageSize), totalCount: sorted.length, page: shown };
}
