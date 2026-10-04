"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

import { tenantKey } from "./tenant-api";

export type ImportEntity = components["schemas"]["ImportEntityResponse"];
export type ImportField = components["schemas"]["ImportFieldResponse"];
export type ImportType = components["schemas"]["ImportTypeResponse"];
export type ImportJob = components["schemas"]["ImportJobResponse"];
export type ImportRow = components["schemas"]["ImportRowResponse"];

export const IMPORT_JOB_STATUSES = [
  "Pending",
  "Validating",
  "AwaitingConfirmation",
  "Processing",
  "Completed",
  "Failed",
  "Cancelled",
] as const;

export const IMPORT_ROW_STATUSES = ["Valid", "Invalid", "Imported", "Failed"] as const;

/** Legacy limit (F19): the API refuses bigger files too. */
export const MAX_IMPORT_FILE_BYTES = 10 * 1024 * 1024;

/** Imports are technical endpoints of the tenant: a tenant-scoped platform token (D-18, D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

/** Under `tenantKey(slug, "imports")`, invalidated as a whole after every change. */
export const importsKey = (slug: string, entity: string, params?: Record<string, unknown>) =>
  queryKey("platform", "tenants", `${slug}:imports`, { entity, ...params });

/** The Worker is working on it: the console refreshes it (F19, legacy every 2 s). */
export const isRunning = (status: string) =>
  status === "Pending" || status === "Validating" || status === "Processing";

/** Only finished imports can be deleted. */
export const isFinished = (status: string) =>
  status === "Completed" || status === "Failed" || status === "Cancelled";

export function statusVariant(status: string): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Completed":
    case "Imported":
    case "Valid":
      return "default";
    case "Failed":
    case "Invalid":
      return "destructive";
    case "AwaitingConfirmation":
      return "secondary";
    default:
      return "outline";
  }
}

export function useImportEntities(slug: string) {
  return useQuery({
    queryKey: importsKey(slug, "entities"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/imports/entities")),
    staleTime: Number.POSITIVE_INFINITY,
  });
}

export function useImportTypes(slug: string) {
  return useQuery({
    queryKey: importsKey(slug, "types"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/imports/types")),
  });
}

export function useImports(slug: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: importsKey(slug, "jobs", { page, pageSize }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/imports", { params: { query: { page, pageSize } } }),
      ),
    refetchInterval: (query) =>
      query.state.data?.items.some((job) => isRunning(job.status)) ? 2000 : false,
  });
}

export function useImport(slug: string, id: string) {
  return useQuery({
    queryKey: importsKey(slug, "job", { id }),
    queryFn: async () =>
      unwrap(await tenantClient(slug).GET("/api/v1/imports/{id}", { params: { path: { id } } })),
    refetchInterval: (query) =>
      query.state.data && isRunning(query.state.data.status) ? 2000 : false,
  });
}

export function useImportRows(
  slug: string,
  id: string,
  status: string | undefined,
  page: number,
  pageSize: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: importsKey(slug, "rows", { id, status, page, pageSize }),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/imports/{id}/rows", {
          params: { path: { id }, query: { status, page, pageSize } },
        }),
      ),
    enabled,
  });
}

export function useImportMutation<TInput, TOutput>(
  slug: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "imports") }),
  });
}

export async function createImportType(slug: string, name: string, targetEntity: string) {
  return unwrap(
    await tenantClient(slug).POST("/api/v1/imports/types", {
      body: { name: name.trim() === "" ? null : name.trim(), targetEntity },
    }),
  );
}

export async function deleteImportType(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/imports/types/{id}", { params: { path: { id } } }),
  );
}

/** Uploads the workbook: the Worker validates it (202). */
export async function startImport(slug: string, name: string, importTypeId: string, file: File) {
  const form = new FormData();
  form.append("name", name);
  form.append("importTypeId", importTypeId);
  form.append("file", file);
  return unwrap(await tenantClient(slug).POST("/api/v1/imports", { body: form as never }));
}

export async function confirmImport(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).POST("/api/v1/imports/{id}/confirm", { params: { path: { id } } }),
  );
}

export async function cancelImport(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).POST("/api/v1/imports/{id}/cancel", { params: { path: { id } } }),
  );
}

export async function deleteImport(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/imports/{id}", { params: { path: { id } } }),
  );
}

/** Downloads the Excel template of a type and saves it in the browser. */
export async function downloadTemplate(slug: string, type: ImportType) {
  const blob = unwrap(
    await tenantClient(slug).GET("/api/v1/imports/types/{id}/template", {
      params: { path: { id: type.id } },
      parseAs: "blob",
    }),
  ) as Blob;
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = templateFileName(type.name);
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
}

/** The same name the API gives (`Content-Disposition`), for the browser download. */
export const templateFileName = (typeName: string) =>
  `${typeName.replace(/[^\p{L}\p{N}]/gu, "_")}_template.xlsx`;
