"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiErrorFrom, networkError, unwrap, type components } from "@auxilia/api-client";

import { createBffClient, BFF_PREFIX } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";
import { CSRF_HEADER, CSRF_HEADER_VALUE } from "@/lib/bff/csrf";

export type DocumentListItem = components["schemas"]["DocumentListItemResponse"];
export type DocumentDetail = components["schemas"]["DocumentResponse"];
export type DocumentArea = components["schemas"]["DocumentAreaResponse"];
export type UpdateDocument = components["schemas"]["UpdateDocumentRequest"];
export type UploadDocumentsResult = components["schemas"]["UploadDocumentsResponse"];

/** Query of the document lists (F14): the legacy filters and sorts, plus client / case / folder for the 360° tabs. */
export type DocumentListParams = {
  page: number;
  pageSize: number;
  sort?: string;
  "filter[clientId]"?: string;
  "filter[caseId]"?: string;
  "filter[folderId]"?: string;
  "filter[clientName]"?: string;
  "filter[fileName]"?: string;
  "filter[description]"?: string;
  "filter[uploadedBy]"?: string;
  "filter[referenceYear]"?: number;
  "filter[areaId]"?: string;
};

/** The custom field entity of documents (F20, `DocumentManager.CustomFieldEntity`). */
export const DOCUMENT_ENTITY = "document";

/** Types the browser shows inline (the API sends any other type as an attachment). */
export const PREVIEW_TYPES = [
  "application/pdf",
  "image/jpeg",
  "image/png",
  "image/gif",
  "image/webp",
];

const api = () => createBffClient("tenant");

/** Everything about documents lives under `documentsKey(tenant)`: a change invalidates it as a whole. */
export const documentsKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "documents", "documents")
    : queryKey(tenant, "documents", "documents", { entity, ...params });

/** The BFF URL of a document's file (`inline` previews PDF and images). */
export const documentContentUrl = (id: string, inline = false) =>
  `${BFF_PREFIX.tenant}documents/${id}/content${inline ? "?inline=true" : ""}`;

/** The BFF URL of the ZIP of a case, or of one of its folders (F33). */
export const caseZipUrl = (caseId: string, folderId?: string) =>
  `${BFF_PREFIX.tenant}documents/zip?caseId=${encodeURIComponent(caseId)}${folderId ? `&folderId=${encodeURIComponent(folderId)}` : ""}`;

/**
 * The lists refresh while a document is still being checked by the Worker (status `Processing`, F14): until the
 * realtime client arrives (B-21) a short poll replaces the `DocumentProcessed` event.
 */
const PROCESSING_POLL_MS = 3000;

export function useDocuments(tenant: string, params: DocumentListParams) {
  return useQuery({
    queryKey: documentsKey(tenant, "list", params),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/documents", { params: { query: params } })),
    placeholderData: keepPreviousData,
    refetchInterval: (query) =>
      query.state.data?.items.some((item) => item.status === "Processing")
        ? PROCESSING_POLL_MS
        : false,
  });
}

export function useDocument(tenant: string, id: string | undefined) {
  return useQuery({
    queryKey: documentsKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/documents/{id}", { params: { path: { id: id! } } })),
    enabled: id !== undefined,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.status === "Processing" ? PROCESSING_POLL_MS : false,
  });
}

export function useDocumentAreas(tenant: string) {
  return useQuery({
    queryKey: documentsKey(tenant, "areas"),
    queryFn: async () => unwrap(await api().GET("/api/v1/document-areas")),
    staleTime: 60_000,
  });
}

/** A mutation of documents that refreshes every document query of the tenant once settled. */
export function useDocumentMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: documentsKey(tenant) }),
  });
}

export async function updateDocument(id: string, body: UpdateDocument) {
  return unwrap(await api().PUT("/api/v1/documents/{id}", { params: { path: { id } }, body }));
}

export async function moveDocument(id: string, folderId: string | null) {
  return unwrap(
    await api().PUT("/api/v1/documents/{id}/folder", {
      params: { path: { id } },
      body: { folderId },
    }),
  );
}

export async function deleteDocument(id: string) {
  await unwrap(await api().DELETE("/api/v1/documents/{id}", { params: { path: { id } } }));
}

export async function createDocumentArea(name: string) {
  return unwrap(await api().POST("/api/v1/document-areas", { body: { name } }));
}

export async function updateDocumentArea(id: string, name: string, isActive: boolean) {
  return unwrap(
    await api().PUT("/api/v1/document-areas/{id}", {
      params: { path: { id } },
      body: { name, isActive },
    }),
  );
}

/** The metadata of an upload (F14): every file gets them; `fileName` only with one file. */
export type UploadMetadata = {
  clientId: string;
  caseId?: string;
  folderId?: string;
  referenceYear: number;
  areaId?: string;
  description?: string;
  fileName?: string;
};

/**
 * Uploads files to the BFF as multipart/form-data with progress (0–1). XMLHttpRequest because fetch has no upload
 * progress; the session cookie and the CSRF header go as for every mutation.
 */
export function uploadDocuments(
  files: readonly File[],
  metadata: UploadMetadata,
  onProgress?: (fraction: number) => void,
  signal?: AbortSignal,
): Promise<UploadDocumentsResult> {
  const form = new FormData();
  for (const [name, value] of Object.entries(metadata)) {
    if (value !== undefined && value !== "") {
      form.append(name, String(value));
    }
  }

  for (const file of files) {
    form.append("files", file, file.name);
  }

  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open("POST", `${BFF_PREFIX.tenant}documents`);
    request.setRequestHeader(CSRF_HEADER, CSRF_HEADER_VALUE);
    request.responseType = "json";
    request.withCredentials = true;
    request.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress?.(event.loaded / event.total);
      }
    };
    request.onload = () => {
      if (request.status >= 200 && request.status < 300) {
        resolve(request.response as UploadDocumentsResult);
      } else {
        reject(apiErrorFrom(new Response(null, { status: request.status }), request.response));
      }
    };
    request.onerror = () => reject(networkError(new Error("Upload failed")));
    request.onabort = () => reject(networkError(new Error("Upload aborted")));
    signal?.addEventListener("abort", () => request.abort(), { once: true });
    request.send(form);
  });
}
