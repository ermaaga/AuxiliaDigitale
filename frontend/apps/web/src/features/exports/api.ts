"use client";

import { useQuery } from "@tanstack/react-query";
import { apiErrorFrom, networkError, unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type ExportJob = components["schemas"]["ExportJobResponse"];
export type ExportSource = components["schemas"]["ExportSourceResponse"];
export type ExportFormat = "csv" | "xlsx" | "pdf";

export const EXPORT_FORMATS: readonly ExportFormat[] = ["csv", "xlsx", "pdf"];

const api = () => createBffClient("tenant");

export const exportsKey = (tenant: string, entity?: string) =>
  entity === undefined
    ? queryKey(tenant, "reporting", "exports")
    : queryKey(tenant, "reporting", "exports", { entity });

/** The lists the user may export, with their columns (one call for every table of the session). */
export function useExportSources(tenant: string) {
  return useQuery({
    queryKey: exportsKey(tenant, "sources"),
    queryFn: async () => unwrap(await api().GET("/api/v1/exports/sources")),
    staleTime: 5 * 60_000,
  });
}

export function useMyExports(tenant: string) {
  return useQuery({
    queryKey: exportsKey(tenant, "mine"),
    queryFn: async () => unwrap(await api().GET("/api/v1/exports")),
    // A queued export becomes ready in the Worker: refresh while one is still queued.
    refetchInterval: (query) =>
      query.state.data?.some((job) => job.status === "Queued") ? 5_000 : false,
  });
}

/**
 * The query string of an export: the list's own parameters (filters, sort, view, box…), the format, the visible
 * columns the list can export, the selected rows and the reader's language. Paging is the export's business.
 */
export function exportQuery(
  params: Record<string, unknown>,
  format: ExportFormat,
  columns: readonly string[],
  language: string,
  ids?: readonly string[],
): URLSearchParams {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(params)) {
    if (
      value !== undefined &&
      value !== null &&
      value !== "" &&
      name !== "page" &&
      name !== "pageSize"
    ) {
      query.set(name, String(value));
    }
  }

  query.set("format", format);
  if (columns.length > 0) {
    query.set("columns", columns.join(","));
  }

  if (ids && ids.length > 0) {
    query.set("ids", ids.join(","));
  }

  query.set("language", language);
  return query;
}

/** The file name of a download (`Content-Disposition`), RFC 5987 form first. */
export function fileNameOf(disposition: string | null, fallback: string): string {
  if (!disposition) {
    return fallback;
  }

  const extended = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (extended?.[1]) {
    return decodeURIComponent(extended[1]);
  }

  const plain = /filename="?([^";]+)"?/i.exec(disposition);
  return plain?.[1] ?? fallback;
}

export type ExportResult =
  { kind: "file"; fileName: string } | { kind: "queued"; rowCount: number };

/** Gets a BFF file and saves it in the browser; 202 means the export was queued for the Worker. */
export async function download(url: string, fallbackName: string): Promise<ExportResult> {
  let response: Response;
  try {
    response = await fetch(url, { credentials: "same-origin" });
  } catch (error) {
    throw networkError(error);
  }

  if (response.status === 202) {
    const body = (await response.json()) as { rowCount: number | string };
    return { kind: "queued", rowCount: Number(body.rowCount) };
  }

  if (!response.ok) {
    const text = await response.text();
    let body: unknown;
    try {
      body = text ? JSON.parse(text) : undefined;
    } catch {
      body = undefined;
    }

    throw apiErrorFrom(response, body);
  }

  const fileName = fileNameOf(response.headers.get("content-disposition"), fallbackName);
  const blob = await response.blob();
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
  return { kind: "file", fileName };
}

export const exportUrl = (source: string, query: URLSearchParams) =>
  `/api/bff/exports/${encodeURIComponent(source)}?${query.toString()}`;

export const exportFileUrl = (id: string) => `/api/bff/exports/${encodeURIComponent(id)}/file`;
