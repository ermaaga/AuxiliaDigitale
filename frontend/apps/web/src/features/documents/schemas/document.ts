import { z } from "zod";

/** Server limits (`Document` in the API, F14, Q50). */
export const FILE_NAME_MAX = 200;
export const DESCRIPTION_MAX = 1000;
export const YEARS_BACK = 10;
export const MAX_FILES = 50;

/** The years an upload or an edit may choose: this year back to ten years ago, and next year (Q50). */
export function referenceYears(today = new Date()): number[] {
  const year = today.getFullYear();
  return Array.from({ length: YEARS_BACK + 2 }, (_, index) => year + 1 - index);
}

/** The name without its extension and the extension (with the dot), as the legacy detail edits it. */
export function splitFileName(fileName: string): { base: string; extension: string } {
  const dot = fileName.lastIndexOf(".");
  return dot > 0
    ? { base: fileName.slice(0, dot), extension: fileName.slice(dot) }
    : { base: fileName, extension: "" };
}

/** Human size (B, KB, MB, GB) in the locale of the page. */
export function formatSize(bytes: number, locale: string): string {
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }

  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`;
}

/** Edit of the metadata (legacy detail): name without the extension, year, area, description. */
export const editDocumentSchema = z.object({
  baseName: z
    .string()
    .trim()
    .min(1, "validation.documents.fileName")
    .max(FILE_NAME_MAX - 10, "validation.documents.fileName"),
  referenceYear: z.coerce.number().int(),
  areaId: z.string(),
  description: z.string().max(DESCRIPTION_MAX, "validation.documents.description"),
});

export type EditDocumentValues = z.infer<typeof editDocumentSchema>;

/** Metadata of an upload (the files are checked by the API: type, content, size). */
export const uploadSchema = z.object({
  clientId: z.string().min(1, "validation.documents.client"),
  referenceYear: z.coerce.number().int(),
  areaId: z.string(),
  description: z.string().max(DESCRIPTION_MAX, "validation.documents.description"),
  fileName: z.string().max(FILE_NAME_MAX - 10, "validation.documents.fileName"),
});

export type UploadValues = z.infer<typeof uploadSchema>;
