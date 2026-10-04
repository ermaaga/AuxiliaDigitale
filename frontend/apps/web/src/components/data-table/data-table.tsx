"use client";

import * as React from "react";
import {
  columnOrderingFeature,
  columnVisibilityFeature,
  createColumnHelper,
  tableFeatures,
  useTable,
  type ColumnDef,
  type ColumnVisibilityState,
  type RowData,
} from "@tanstack/react-table";
import {
  ArrowDownIcon,
  ArrowUpDownIcon,
  ArrowUpIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  Columns3Icon,
  InboxIcon,
} from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { cn } from "@auxilia/ui/lib/utils";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@auxilia/ui/components/table";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";

import { nextSort, pageCount, sortDirection, type DataTableColumn } from "./table-model";
import { PAGE_SIZES } from "./use-table-state";

const features = tableFeatures({ columnVisibilityFeature, columnOrderingFeature });

export type DataTableProps<TRow extends RowData> = {
  /** Accessible name of the table (usually the page title). */
  label: string;
  columns: readonly DataTableColumn<TRow>[];
  rows: readonly TRow[] | undefined;
  getRowId: (row: TRow) => string;
  totalCount: number;
  page: number;
  pageSize: number;
  sort: string | null;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  onSortChange: (sort: string | null) => void;
  isLoading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  /** Search box and filters, above the table. */
  toolbar?: React.ReactNode;
  /** A filter is active: the empty state says "no results match the filters". */
  filtered?: boolean;
  /** Columns hidden at start (grid layout of the role, F21). */
  initiallyHidden?: readonly string[];
  /** Export of the current filters (F26): rendered with the ids of the visible columns (feature `exports`). */
  exportMenu?: (visibleColumnIds: readonly string[]) => React.ReactNode;
  onRowClick?: (row: TRow) => void;
  /** A short list shown whole (e.g. the modules of a tenant): no page size and page buttons, the total stays. */
  hidePaging?: boolean;
};

/**
 * Server-side list (skill auxilia-frontend-feature/ui-design, F21, F28, F34): the API pages, sorts and filters; the
 * table renders one page with sortable headers, a column picker, pagination, loading skeleton, empty and error
 * states, and becomes a list of cards below `md`.
 */
export function DataTable<TRow extends RowData>(props: DataTableProps<TRow>) {
  const t = useTranslations();
  const { columns, rows, isLoading, error } = props;
  const [visibility, setVisibility] = React.useState<ColumnVisibilityState>(() =>
    Object.fromEntries((props.initiallyHidden ?? []).map((id) => [id, false])),
  );

  const helper = React.useMemo(() => createColumnHelper<typeof features, TRow>(), []);
  const definitions = React.useMemo<ColumnDef<typeof features, TRow>[]>(
    () =>
      columns.map((column) =>
        helper.display({
          id: column.id,
          header: column.header,
          cell: (info) => column.cell(info.row.original),
        }),
      ),
    [columns, helper],
  );

  const table = useTable({
    features,
    columns: definitions,
    data: (rows ?? []) as TRow[],
    getRowId: props.getRowId,
    state: { columnVisibility: visibility },
    onColumnVisibilityChange: setVisibility,
  });

  const visible = columns.filter((column) => visibility[column.id] !== false);
  const pages = pageCount(props.totalCount, props.pageSize);
  const showEmpty = !isLoading && !error && (rows?.length ?? 0) === 0;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end gap-2">
        <div className="flex flex-1 flex-wrap items-end gap-2">{props.toolbar}</div>
        <div className="flex items-center gap-2">
          {props.exportMenu ? props.exportMenu(visible.map((column) => column.id)) : null}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button type="button" variant="outline" size="sm">
                <Columns3Icon aria-hidden /> {t("common.table.columns")}
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuLabel>{t("common.table.columns")}</DropdownMenuLabel>
              {columns
                .filter((column) => column.hideable !== false)
                .map((column) => (
                  <DropdownMenuCheckboxItem
                    key={column.id}
                    checked={visibility[column.id] !== false}
                    onSelect={(event) => event.preventDefault()}
                    onCheckedChange={(checked) =>
                      setVisibility((current) => ({ ...current, [column.id]: checked === true }))
                    }
                  >
                    {column.header}
                  </DropdownMenuCheckboxItem>
                ))}
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      {error ? <ApiErrorAlert error={error} onRetry={props.onRetry} /> : null}

      <div className="hidden rounded-md border md:block">
        <Table aria-label={props.label} aria-busy={isLoading || undefined}>
          <TableHeader>
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
                {group.headers.map((header) => {
                  const column = columns.find((item) => item.id === header.column.id)!;
                  const direction = column.sortField
                    ? sortDirection(props.sort, column.sortField)
                    : undefined;
                  return (
                    <TableHead
                      key={header.id}
                      aria-sort={
                        direction === "asc"
                          ? "ascending"
                          : direction === "desc"
                            ? "descending"
                            : undefined
                      }
                    >
                      {column.sortField ? (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="-ml-3"
                          aria-label={t("common.table.sortBy", { column: column.header })}
                          onClick={() =>
                            props.onSortChange(nextSort(props.sort, column.sortField!))
                          }
                        >
                          {column.header}
                          {direction === "asc" ? (
                            <ArrowUpIcon aria-hidden />
                          ) : direction === "desc" ? (
                            <ArrowDownIcon aria-hidden />
                          ) : (
                            <ArrowUpDownIcon aria-hidden className="opacity-50" />
                          )}
                        </Button>
                      ) : (
                        column.header
                      )}
                    </TableHead>
                  );
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading
              ? Array.from({ length: 5 }, (_, index) => (
                  <TableRow key={`skeleton-${index}`}>
                    {visible.map((column) => (
                      <TableCell key={column.id}>
                        <Skeleton className="h-4 w-full" />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              : table.getRowModel().rows.map((row) => (
                  <TableRow
                    key={row.id}
                    className={props.onRowClick ? "cursor-pointer" : undefined}
                    onClick={props.onRowClick ? () => props.onRowClick!(row.original) : undefined}
                  >
                    {row.getVisibleCells().map((cell) => (
                      <TableCell key={cell.id}>
                        <table.FlexRender cell={cell} />
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
          </TableBody>
        </Table>
      </div>

      <ul
        className="flex flex-col gap-2 md:hidden"
        aria-label={props.label}
        aria-busy={isLoading || undefined}
      >
        {isLoading
          ? Array.from({ length: 3 }, (_, index) => (
              <li key={`skeleton-${index}`} className="rounded-md border p-3">
                <Skeleton className="mb-2 h-4 w-1/2" />
                <Skeleton className="h-4 w-3/4" />
              </li>
            ))
          : (rows ?? []).map((row) => (
              <li
                key={props.getRowId(row)}
                className="rounded-md border p-3"
                onClick={props.onRowClick ? () => props.onRowClick!(row) : undefined}
              >
                {visible
                  .filter((column) => column.mobile === "title")
                  .map((column) => (
                    <div key={column.id} className="font-medium">
                      {column.cell(row)}
                    </div>
                  ))}
                <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
                  {visible
                    .filter((column) => (column.mobile ?? "detail") === "detail")
                    .map((column) => (
                      <React.Fragment key={column.id}>
                        <dt className="text-muted-foreground">{column.header}</dt>
                        <dd className="min-w-0 break-words">{column.cell(row)}</dd>
                      </React.Fragment>
                    ))}
                </dl>
              </li>
            ))}
      </ul>

      {showEmpty ? (
        <div className="flex flex-col items-center gap-2 rounded-md border border-dashed p-8 text-center text-muted-foreground">
          <InboxIcon aria-hidden className="size-8" />
          <p>{props.filtered ? t("common.table.emptyFiltered") : t("common.table.empty")}</p>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <p className="text-muted-foreground" aria-live="polite">
          {t("common.table.total", { count: props.totalCount })}
        </p>
        <div className={cn("flex items-center gap-2", props.hidePaging && "hidden")}>
          <label className="flex items-center gap-2 text-muted-foreground">
            <span>{t("common.table.rowsPerPage")}</span>
            <Select
              value={String(props.pageSize)}
              onValueChange={(value) => props.onPageSizeChange(Number(value))}
            >
              <SelectTrigger size="sm" className="w-20">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PAGE_SIZES.map((size) => (
                  <SelectItem key={size} value={String(size)}>
                    {size}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </label>
          <span>{t("common.table.pageOf", { page: props.page, pages })}</span>
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("common.table.previousPage")}
            disabled={props.page <= 1 || isLoading}
            onClick={() => props.onPageChange(props.page - 1)}
          >
            <ChevronLeftIcon aria-hidden />
          </Button>
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("common.table.nextPage")}
            disabled={props.page >= pages || isLoading}
            onClick={() => props.onPageChange(props.page + 1)}
          >
            <ChevronRightIcon aria-hidden />
          </Button>
        </div>
      </div>
    </div>
  );
}
