import type { DocumentDetail } from "./api";

/**
 * What the detail drawer previews (F14, ADR 0020): images through `<img>`, PDFs drawn by PDF.js on a canvas, plain
 * text in a `<pre>`; every other type (office files, archives, signed files, HEIC/TIFF) is only downloaded.
 */
export type PreviewKind = "image" | "pdf" | "text";

const KINDS: Record<string, PreviewKind> = {
  "image/jpeg": "image",
  "image/png": "image",
  "image/gif": "image",
  "image/webp": "image",
  "application/pdf": "pdf",
  "text/plain": "text",
  "text/csv": "text",
  "application/xml": "text",
};

/** Bigger PDFs are not loaded into the drawer (memory of phones): they still open in a new tab. */
export const PDF_PREVIEW_MAX_BYTES = 20 * 1024 * 1024;

/** Only the beginning of a text file is read and shown. */
export const TEXT_PREVIEW_BYTES = 200 * 1024;

/** Largest canvas side drawn by the PDF preview, in device pixels. */
export const MAX_CANVAS_SIDE = 4096;

/** The preview of a document, or `null` when it has none (a damaged file is never opened). */
export function previewKind(
  document: Pick<DocumentDetail, "contentType" | "status">,
): PreviewKind | null {
  return document.status === "Damaged" ? null : (KINDS[document.contentType] ?? null);
}

/**
 * The first `limit` bytes of a text response, decoded as UTF-8 or, when they are not valid UTF-8, as Windows-1252 (the
 * text files of Italian offices). The rest of the body is never downloaded.
 */
export async function readTextPreview(
  response: Response,
  limit = TEXT_PREVIEW_BYTES,
): Promise<{ text: string; truncated: boolean }> {
  const bytes = new Uint8Array(limit);
  let length = 0;
  let truncated = false;
  const reader = response.body?.getReader();
  while (reader) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    const room = limit - length;
    bytes.set(value.subarray(0, room), length);
    length += Math.min(room, value.length);
    if (value.length > room) {
      truncated = true;
      await reader.cancel();
      break;
    }
  }

  const content = bytes.subarray(0, length);
  try {
    // `stream` keeps a character cut by the limit out of the text instead of failing on it.
    return {
      text: new TextDecoder("utf-8", { fatal: true }).decode(content, { stream: truncated }),
      truncated,
    };
  } catch {
    return { text: new TextDecoder("windows-1252").decode(content), truncated };
  }
}

/**
 * The CSS scale that fits a page of `pageWidth` points into `containerWidth` pixels, and the device pixel ratio to draw
 * it with, lowered so that the canvas never exceeds `MAX_CANVAS_SIDE` on either side.
 */
export function pageScale(
  page: { width: number; height: number },
  containerWidth: number,
  devicePixelRatio: number,
): { scale: number; pixelRatio: number } {
  const scale = containerWidth > 0 && page.width > 0 ? containerWidth / page.width : 1;
  const largest = Math.max(page.width, page.height) * scale;
  const pixelRatio = Math.max(Math.min(devicePixelRatio || 1, MAX_CANVAS_SIDE / largest), 0.1);
  return { scale, pixelRatio };
}
