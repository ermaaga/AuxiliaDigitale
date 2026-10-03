import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { isApiError } from "@auxilia/api-client";

import { queryKey } from "@/lib/api/query-keys";

import { caseZipUrl, documentContentUrl, documentsKey, uploadDocuments } from "./api";
import { editDocumentSchema, formatSize, referenceYears, splitFileName } from "./schemas/document";

describe("document helpers", () => {
  it("offers this year back to ten years ago, and next year (Q50)", () => {
    expect(referenceYears(new Date(2026, 9, 3))).toEqual([
      2027, 2026, 2025, 2024, 2023, 2022, 2021, 2020, 2019, 2018, 2017, 2016,
    ]);
  });

  it("splits the extension off the name as the legacy detail edits it", () => {
    expect(splitFileName("dichiarazione.2025.pdf")).toEqual({
      base: "dichiarazione.2025",
      extension: ".pdf",
    });
    expect(splitFileName("README")).toEqual({ base: "README", extension: "" });
    expect(splitFileName(".env")).toEqual({ base: ".env", extension: "" });
  });

  it("formats sizes in the page language", () => {
    expect(formatSize(512, "it")).toBe("512 B");
    expect(formatSize(1536, "it")).toBe("1,5 KB");
    expect(formatSize(5 * 1024 * 1024, "en")).toBe("5 MB");
  });

  it("builds the BFF urls and the query keys", () => {
    expect(documentContentUrl("d1")).toBe("/api/bff/documents/d1/content");
    expect(documentContentUrl("d1", true)).toBe("/api/bff/documents/d1/content?inline=true");
    expect(caseZipUrl("c1", "f 1")).toBe("/api/bff/documents/zip?caseId=c1&folderId=f%201");
    expect(documentsKey("demo")).toEqual(queryKey("demo", "documents", "documents"));
  });

  it("validates the metadata edit with the API's translation keys", () => {
    const result = editDocumentSchema.safeParse({
      baseName: " ",
      referenceYear: "2025",
      areaId: "none",
      description: "x".repeat(1001),
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((issue) => [issue.path[0], issue.message])).toEqual([
      ["baseName", "validation.documents.fileName"],
      ["description", "validation.documents.description"],
    ]);
  });
});

/** A minimal XMLHttpRequest (the tests run in Node): records the request and answers with the given status and body. */
class FakeXhr {
  static last: FakeXhr | undefined;
  static answer: { status: number; body: unknown } = { status: 201, body: {} };

  method = "";
  url = "";
  headers: Record<string, string> = {};
  body: FormData | undefined;
  status = 0;
  response: unknown;
  responseType = "";
  withCredentials = false;
  upload: {
    onprogress?: (event: { lengthComputable: boolean; loaded: number; total: number }) => void;
  } = {};
  onload?: () => void;
  onerror?: () => void;
  onabort?: () => void;

  open(method: string, url: string) {
    this.method = method;
    this.url = url;
  }

  setRequestHeader(name: string, value: string) {
    this.headers[name] = value;
  }

  send(body: FormData) {
    FakeXhr.last = this;
    this.body = body;
    this.upload.onprogress?.({ lengthComputable: true, loaded: 5, total: 10 });
    this.status = FakeXhr.answer.status;
    this.response = FakeXhr.answer.body;
    this.onload?.();
  }

  abort() {
    this.onabort?.();
  }
}

describe("uploadDocuments", () => {
  beforeEach(() => vi.stubGlobal("XMLHttpRequest", FakeXhr));
  afterEach(() => vi.unstubAllGlobals());

  it("posts the files and the metadata as multipart with the CSRF header and reports progress", async () => {
    FakeXhr.answer = { status: 201, body: { documentIds: ["d1", "d2"] } };
    const progress: number[] = [];

    const result = await uploadDocuments(
      [new File(["%PDF"], "a.pdf"), new File(["%PDF"], "b.pdf")],
      { clientId: "c1", referenceYear: 2026, description: "", caseId: undefined },
      (fraction) => progress.push(fraction),
    );

    const request = FakeXhr.last!;
    expect(result.documentIds).toEqual(["d1", "d2"]);
    expect([
      request.method,
      request.url,
      request.headers["x-requested-with"],
      request.withCredentials,
    ]).toEqual(["POST", "/api/bff/documents", "auxilia", true]);
    expect([...request.body!.entries()].filter(([, value]) => typeof value === "string")).toEqual([
      ["clientId", "c1"],
      ["referenceYear", "2026"],
    ]);
    expect(request.body!.getAll("files").map((file) => (file as File).name)).toEqual([
      "a.pdf",
      "b.pdf",
    ]);
    expect(progress).toEqual([0.5]);
  });

  it("turns a ProblemDetails into an ApiError with the field errors", async () => {
    FakeXhr.answer = {
      status: 400,
      body: {
        status: 400,
        errorCode: "AUX-16001",
        errors: { "files[1]": ["validation.documents.fileType"] },
      },
    };

    const failure = await uploadDocuments([new File(["x"], "a.exe")], {
      clientId: "c1",
      referenceYear: 2026,
    }).catch((error: unknown) => error);

    expect(isApiError(failure)).toBe(true);
    if (isApiError(failure)) {
      expect([failure.status, failure.errorCode]).toEqual([400, "AUX-16001"]);
      expect(failure.fieldErrors).toEqual({ "files[1]": ["validation.documents.fileType"] });
    }
  });
});
