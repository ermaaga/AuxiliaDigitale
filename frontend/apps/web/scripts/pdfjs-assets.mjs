// Static files of the document preview (F14, ADR 0020): the PDF.js worker and its image decoders, copied from the
// installed `pdfjs-dist` into public/static/pdfjs (git-ignored; "static" is a reserved tenant slug) before `dev` and
// `build`, so the version always matches the library and nothing is loaded from another host.
// Left out on purpose: `quickjs-eval` (the engine of the scripts embedded in PDFs: the preview never runs them), the
// CMaps (CJK fonts) and the standard fonts (Liberation is GPL with a font exception, outside the licence allowlist;
// PDF.js falls back to the browser's system fonts).
import { copyFile, mkdir, rm } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const PDFJS_FILES = [
  // The legacy build, like the library imported by the preview (polyfills for browsers that are not the newest).
  "legacy/build/pdf.worker.min.mjs",
  // JPEG 2000 and JBIG2 images (scanned documents) and ICC colour profiles, with the plain-JS fallbacks.
  "wasm/openjpeg.wasm",
  "wasm/openjpeg_nowasm_fallback.js",
  "wasm/jbig2.wasm",
  "wasm/jbig2_nowasm_fallback.js",
  "wasm/qcms_bg.wasm",
  "wasm/LICENSE_OPENJPEG",
  "wasm/LICENSE_PDFJS_OPENJPEG",
  "wasm/LICENSE_JBIG2",
  "wasm/LICENSE_PDFJS_JBIG2",
  "wasm/LICENSE_QCMS",
  "wasm/LICENSE_PDFJS_QCMS",
  "LICENSE",
];

const require = createRequire(import.meta.url);
const source = dirname(require.resolve("pdfjs-dist/package.json"));
const target = fileURLToPath(new URL("../public/static/pdfjs/", import.meta.url));

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  await rm(target, { recursive: true, force: true });
  for (const file of PDFJS_FILES) {
    const destination = join(target, file.replace(/^legacy\/build\//, ""));
    await mkdir(dirname(destination), { recursive: true });
    await copyFile(join(source, file), destination);
  }
}
