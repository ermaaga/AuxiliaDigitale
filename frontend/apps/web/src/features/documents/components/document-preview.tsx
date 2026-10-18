"use client";

import * as React from "react";
import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { queryKey } from "@/lib/api/query-keys";

import { documentContentUrl, type DocumentDetail } from "../api";
import { PDF_PREVIEW_MAX_BYTES, previewKind, readTextPreview } from "../preview";

// PDF.js is heavy and browser-only: loaded on demand (skill auxilia-frontend-feature).
const PdfPreview = dynamic(() => import("./pdf-preview"), {
  ssr: false,
  loading: () => <Skeleton className="h-72 w-full" />,
});

/**
 * The preview at the top of the document drawer (F14, ADR 0020): images, PDFs (PDF.js) and the beginning of text
 * files; nothing for the other types, which are downloaded.
 */
export function DocumentPreview({
  tenant,
  document,
}: {
  tenant: string;
  document: DocumentDetail;
}) {
  const t = useTranslations();
  switch (previewKind(document)) {
    case "image":
      return (
        // The API serves the file with `Content-Disposition: inline` only for the image types it lets a page show.
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={documentContentUrl(document.id, true)}
          alt={document.fileName}
          className="max-h-72 w-full rounded-md border object-contain"
        />
      );
    case "pdf":
      return Number(document.size) > PDF_PREVIEW_MAX_BYTES ? (
        <Alert>
          <AlertDescription>{t("app.documents.viewer.tooLarge")}</AlertDescription>
        </Alert>
      ) : (
        <PdfPreview url={documentContentUrl(document.id)} fileName={document.fileName} />
      );
    case "text":
      return <TextPreview tenant={tenant} document={document} />;
    default:
      return null;
  }
}

/** The beginning of a text file, shown as text (never as markup). */
function TextPreview({ tenant, document }: { tenant: string; document: DocumentDetail }) {
  const t = useTranslations();
  const preview = useQuery({
    // Outside `documentsKey`: the content of a version (SHA-256) never changes, and a deleted document is not re-read.
    queryKey: queryKey(tenant, "documents", "preview", {
      id: document.id,
      sha256: document.sha256,
    }),
    queryFn: async ({ signal }) => {
      const response = await fetch(documentContentUrl(document.id), { signal });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      return readTextPreview(response);
    },
    staleTime: Infinity,
  });

  if (preview.isError) {
    return (
      <Alert>
        <AlertDescription>{t("app.documents.viewer.failed")}</AlertDescription>
      </Alert>
    );
  }

  if (!preview.data) {
    return <Skeleton className="h-40 w-full" />;
  }

  return (
    <section
      aria-label={t("app.documents.viewer.title", { name: document.fileName })}
      className="flex flex-col gap-1"
    >
      <pre
        tabIndex={0}
        className="max-h-72 overflow-auto rounded-md border bg-muted p-3 font-mono text-xs whitespace-pre-wrap break-words"
      >
        {preview.data.text}
      </pre>
      {preview.data.truncated ? (
        <p className="text-xs text-muted-foreground">{t("app.documents.viewer.truncated")}</p>
      ) : null}
    </section>
  );
}
