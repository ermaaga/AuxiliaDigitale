import { describe, expect, it } from "vitest";

import { PREVIEW_TYPES } from "./api";
import { MAX_CANVAS_SIDE, pageScale, previewKind, readTextPreview } from "./preview";

const body = (...chunks: Uint8Array[]) =>
  new Response(
    new ReadableStream<Uint8Array>({
      start(controller) {
        chunks.forEach((chunk) => controller.enqueue(chunk));
        controller.close();
      },
    }),
  );

describe("document preview (F14, ADR 0020)", () => {
  it("previews images, PDFs and text files, never a damaged file or another type", () => {
    const kind = (contentType: string, status = "Available") =>
      previewKind({ contentType, status });
    expect(kind("image/png")).toBe("image");
    expect(kind("application/pdf")).toBe("pdf");
    expect(kind("application/pdf", "Processing")).toBe("pdf");
    expect(kind("text/csv")).toBe("text");
    expect(kind("application/xml")).toBe("text");
    expect(kind("application/pdf", "Damaged")).toBeNull();
    for (const type of [
      "image/heic",
      "image/tiff",
      "application/zip",
      "application/pkcs7-mime",
      "image/svg+xml",
    ]) {
      expect(kind(type)).toBeNull();
    }
  });

  it("shows inline only the types the API serves inline", () => {
    const inline = ["image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf"];
    expect([...PREVIEW_TYPES].sort()).toEqual([...inline].sort());
    for (const type of inline) {
      expect(previewKind({ contentType: type, status: "Available" })).not.toBe("text");
    }
  });

  it("reads only the beginning of a text file, without breaking a character", async () => {
    const encoder = new TextEncoder();
    const whole = await readTextPreview(
      body(encoder.encode("nome;città\n"), encoder.encode("Rossi;Forlì\n")),
    );
    expect(whole).toEqual({ text: "nome;città\nRossi;Forlì\n", truncated: false });

    // "città" = c i t t 0xC3 0xA0: the limit falls inside the "à".
    const cut = await readTextPreview(body(encoder.encode("città e altro")), 5);
    expect(cut).toEqual({ text: "citt", truncated: true });
  });

  it("falls back to Windows-1252 for files that are not UTF-8", async () => {
    const latin = new Uint8Array([0x63, 0x69, 0x74, 0x74, 0xe0, 0x20, 0x80]);
    expect(await readTextPreview(body(latin))).toEqual({ text: "città €", truncated: false });
  });

  it("fits the page to the width and caps the canvas size", () => {
    const a4 = { width: 595, height: 842 };
    expect(pageScale(a4, 476, 2)).toEqual({ scale: 0.8, pixelRatio: 2 });

    const poster = { width: 600, height: 20_000 };
    const { scale, pixelRatio } = pageScale(poster, 600, 3);
    expect(scale).toBe(1);
    expect(poster.height * scale * pixelRatio).toBeLessThanOrEqual(MAX_CANVAS_SIDE);
    expect(pageScale(a4, 0, 1).scale).toBe(1);
  });
});
