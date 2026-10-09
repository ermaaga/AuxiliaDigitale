"use client";

import * as React from "react";
import { ChevronLeftIcon, ChevronRightIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  AnnotationMode,
  getDocument,
  GlobalWorkerOptions,
  PasswordResponses,
  type PDFDocumentLoadingTask,
  type PDFDocumentProxy,
  type RenderTask,
} from "pdfjs-dist/legacy/build/pdf.mjs";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Button } from "@auxilia/ui/components/button";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { MAX_CANVAS_SIDE, pageScale } from "../preview";

// The legacy build: the modern one needs JavaScript of the newest browsers (`Map.getOrInsertComputed`, Chrome 140 fails).
// Worker and decoders are served by the app (scripts/pdfjs-assets.mjs, own CSP in next.config.ts).
GlobalWorkerOptions.workerSrc = "/static/pdfjs/pdf.worker.min.mjs";

/** Images inside a PDF bigger than this (pixels) are skipped instead of decoded (memory of crafted files). */
const MAX_IMAGE_PIXELS = 40_000_000;

type State =
  | { status: "loading" }
  | { status: "ready"; pdf: PDFDocumentProxy }
  | { status: "failed"; reason: "protected" | "unreadable" };

/**
 * The PDF preview of the document drawer (F14, ADR 0020): PDF.js draws one page at a time on a canvas, as wide as the
 * drawer. The file comes from the BFF like a download (same access rules); it is never framed, its scripts and forms
 * never run (PDF.js 6 has no `eval`, XFA stays off, annotations are drawn as pictures without links).
 */
export default function PdfPreview({ url, fileName }: { url: string; fileName: string }) {
  const t = useTranslations();
  const [state, setState] = React.useState<State>({ status: "loading" });
  const [pageNumber, setPageNumber] = React.useState(1);
  const container = React.useRef<HTMLDivElement>(null);
  const canvas = React.useRef<HTMLCanvasElement>(null);
  const width = useWidth(container);

  React.useEffect(() => {
    const controller = new AbortController();
    let task: PDFDocumentLoadingTask | undefined;
    let cancelled = false;

    void (async () => {
      try {
        const response = await fetch(url, { signal: controller.signal });
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const data = new Uint8Array(await response.arrayBuffer());
        if (cancelled) {
          return;
        }

        task = getDocument({
          data,
          wasmUrl: "/static/pdfjs/wasm/",
          enableXfa: false,
          maxImageSize: MAX_IMAGE_PIXELS,
          canvasMaxAreaInBytes: MAX_CANVAS_SIDE * MAX_CANVAS_SIDE * 4,
        });
        const loading = task;
        // A password-protected PDF is not opened: no password is asked for, it stays a download.
        loading.onPassword = (_: unknown, reason: number) => {
          if (
            reason === PasswordResponses.NEED_PASSWORD ||
            reason === PasswordResponses.INCORRECT_PASSWORD
          ) {
            if (!cancelled) {
              setState({ status: "failed", reason: "protected" });
            }

            void loading.destroy();
          }
        };
        const pdf = await loading.promise;
        if (!cancelled) {
          setState({ status: "ready", pdf });
        }
      } catch {
        if (!cancelled) {
          setState((current) =>
            current.status === "failed" ? current : { status: "failed", reason: "unreadable" },
          );
        }
      }
    })();

    return () => {
      cancelled = true;
      controller.abort();
      // Ends the document and its worker.
      void task?.destroy();
    };
  }, [url]);

  const pdf = state.status === "ready" ? state.pdf : undefined;

  React.useEffect(() => {
    if (!pdf || !canvas.current || width === 0) {
      return;
    }

    const target = canvas.current;
    let task: RenderTask | undefined;
    let cancelled = false;
    void (async () => {
      try {
        const page = await pdf.getPage(pageNumber);
        if (cancelled) {
          return;
        }

        const natural = page.getViewport({ scale: 1 });
        const { scale, pixelRatio } = pageScale(natural, width, window.devicePixelRatio);
        const viewport = page.getViewport({ scale });
        target.width = Math.floor(viewport.width * pixelRatio);
        target.height = Math.floor(viewport.height * pixelRatio);
        target.style.width = `${Math.floor(viewport.width)}px`;
        target.style.height = `${Math.floor(viewport.height)}px`;
        task = page.render({
          canvas: target,
          viewport,
          transform: pixelRatio === 1 ? undefined : [pixelRatio, 0, 0, pixelRatio, 0, 0],
          annotationMode: AnnotationMode.ENABLE,
        });
        await task.promise;
      } catch {
        // A cancelled render (page or size changed) or a broken page: the canvas keeps what it has.
      }
    })();

    return () => {
      cancelled = true;
      task?.cancel();
    };
  }, [pdf, pageNumber, width]);

  const pages = pdf?.numPages ?? 0;
  const label = t("app.documents.viewer.pageOf", { page: pageNumber, pages });

  return (
    <section
      aria-label={t("app.documents.viewer.title", { name: fileName })}
      className="flex flex-col gap-2"
    >
      {state.status === "failed" ? (
        <Alert>
          <AlertDescription>
            {t(
              state.reason === "protected"
                ? "app.documents.viewer.protected"
                : "app.documents.viewer.failed",
            )}
          </AlertDescription>
        </Alert>
      ) : null}
      <div
        ref={container}
        className={
          state.status === "failed"
            ? "hidden"
            : "max-h-[60vh] overflow-auto rounded-md border bg-muted"
        }
      >
        {state.status === "loading" ? (
          <Skeleton
            className="h-72 w-full"
            aria-busy="true"
            aria-label={t("app.documents.loading")}
          />
        ) : null}
        <canvas
          ref={canvas}
          role="img"
          aria-label={label}
          className={pdf ? "block bg-white" : "hidden"}
        />
      </div>
      {pages > 1 ? (
        <div className="flex items-center justify-between gap-2">
          <Button
            type="button"
            variant="outline"
            size="icon"
            onClick={() => setPageNumber((current) => Math.max(current - 1, 1))}
            disabled={pageNumber <= 1}
            aria-label={t("app.documents.viewer.previous")}
          >
            <ChevronLeftIcon aria-hidden />
          </Button>
          <p className="text-sm text-muted-foreground" aria-live="polite">
            {label}
          </p>
          <Button
            type="button"
            variant="outline"
            size="icon"
            onClick={() => setPageNumber((current) => Math.min(current + 1, pages))}
            disabled={pageNumber >= pages}
            aria-label={t("app.documents.viewer.next")}
          >
            <ChevronRightIcon aria-hidden />
          </Button>
        </div>
      ) : null}
    </section>
  );
}

/** The content width of an element, kept up to date. */
function useWidth(element: React.RefObject<HTMLElement | null>): number {
  const [width, setWidth] = React.useState(0);
  React.useEffect(() => {
    const current = element.current;
    if (!current) {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      setWidth(Math.floor(entry?.contentRect.width ?? 0));
    });
    observer.observe(current);
    return () => observer.disconnect();
  }, [element]);
  return width;
}
