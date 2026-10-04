"use client";

import * as React from "react";
import { useQueryClient } from "@tanstack/react-query";
import { DownloadIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";

import { useNotify } from "@/lib/notify";

import {
  download,
  EXPORT_FORMATS,
  exportQuery,
  exportsKey,
  exportUrl,
  useExportSources,
  type ExportFormat,
} from "../api";

const LABELS: Record<ExportFormat, string> = { csv: "CSV", xlsx: "Excel", pdf: "PDF" };

/**
 * The export of a table (F26, Q38): every row of the current filters and sort (or the selected rows) with the visible
 * columns, as CSV, Excel or PDF. Large exports are queued and announced by a notification. Hidden when the user may
 * not export the list.
 */
export function ExportMenu({
  tenant,
  source,
  params,
  columns,
  ids,
  label,
}: {
  tenant: string;
  source: string;
  params: Record<string, unknown>;
  /** Ids of the visible columns; those the list cannot export are left out. */
  columns: readonly string[];
  ids?: readonly string[];
  label?: string;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const notify = useNotify();
  const client = useQueryClient();
  const sources = useExportSources(tenant);
  const [busy, setBusy] = React.useState(false);
  const definition = sources.data?.find((item) => item.key === source);
  if (!definition) {
    return null;
  }

  const exportable = new Set(definition.columns.map((column) => column.id));
  const chosen = columns.filter((column) => exportable.has(column));

  const run = async (format: ExportFormat) => {
    setBusy(true);
    try {
      const result = await download(
        exportUrl(source, exportQuery(params, format, chosen, locale, ids)),
        `${source}.${format}`,
      );
      if (result.kind === "queued") {
        notify.info("app.exports.queued");
        await client.invalidateQueries({ queryKey: exportsKey(tenant, "mine") });
      }
    } catch (error) {
      notify.error(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" size="sm" disabled={busy}>
          <DownloadIcon aria-hidden /> {label ?? t("common.table.export")}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>
          {ids && ids.length > 0
            ? t("app.exports.selected", { count: ids.length })
            : t("app.exports.allRows")}
        </DropdownMenuLabel>
        {EXPORT_FORMATS.map((format) => (
          <DropdownMenuItem key={format} onSelect={() => void run(format)}>
            {LABELS[format]}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
