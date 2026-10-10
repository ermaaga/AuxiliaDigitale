# ADR 0020 — Document preview in the drawer with PDF.js (`pdfjs-dist`)

- Status: Accepted (2026-10-09, approved by the user) · Related: F14, B-13, ADR 0011, `docs/security/asvs-checklist.md` V12/V14

## Context
The document drawer showed images only; PDFs opened in a new tab (the app never frames files, `X-Frame-Options: DENY`)
and every other type was downloaded. A new tab is not a preview on every device (Chrome on Android downloads the PDF)
and takes the user out of the page. Text files (txt/csv/xml) had no preview at all.

While checking the new-tab path we found that the BFF dropped the API's `Content-Security-Policy` (not in its allowlist
of response headers) and that `src/proxy.ts` does not cover `/api`: a file opened inline was a document of the app's
origin without any CSP.

Options compared: the browser's own viewer in an `iframe` (needs `frame-ancestors`/XFO relaxed, inconsistent across
browsers, nothing on Android); PDF.js drawing on a canvas; previews rendered by the Worker (PDFium native code on the
server, derived files to store); Office → PDF through Gotenberg/LibreOffice (heavy sidecar parsing untrusted files);
Office → HTML in the page (docx-preview, mammoth, SheetJS: markup of the file in our DOM, SheetJS on npm stuck on
versions with known advisories); external viewers or commercial SDKs (personal data to third parties, not open source).

## Decision
1. **PDF**: `pdfjs-dist` 6.3.289 (Apache-2.0, Mozilla), the **legacy build** (`pdfjs-dist/legacy/build/pdf.mjs`): the
   modern build needs JavaScript of the newest browsers (`Map.prototype.getOrInsertComputed`) and drew nothing in
   Chromium 140. `PdfPreview` (`features/documents/components/pdf-preview.tsx`) is loaded on demand, reads the file with
   `fetch` from the BFF (`/documents/{id}/content`, the same access rules as a download) and draws one page at a time on
   a canvas as wide as the drawer (previous/next, "page X of Y"); PDFs over 20 MB are not loaded (new tab or download),
   password-protected PDFs are not opened.
2. **Text** (`text/plain`, `text/csv`, `application/xml`): the first 200 KB in a `<pre>` (React escapes it), UTF-8 or
   Windows-1252; images stay `<img>`. Office files, archives, `.p7m`, HEIC/TIFF stay downloads.
3. **Hardening of PDF.js**: PDF.js 6 has no `eval` (the CVE-2024-4367 class); XFA off; the PDF's scripts never run
   (`quickjs-eval` is not shipped); annotations drawn as pictures without a link layer; `maxImageSize` 40 MP and the
   canvas capped at 4096 px per side (crafted files). The worker and its WebAssembly decoders (OpenJPEG, JBIG2, qcms —
   BSD/Apache/MIT) are copied from the installed package by `scripts/pdfjs-assets.mjs` to `public/static/pdfjs/`
   (git-ignored, before `dev`/`build`; `static` is a reserved tenant slug) and served by the app with their own CSP
   `default-src 'self'; script-src 'self' 'wasm-unsafe-eval'` (next.config.ts; excluded from the proxy): the worker
   parsing the untrusted file can reach only the app. Pages get `worker-src 'self'`. The standard fonts are not shipped
   (Liberation is GPL with a font exception, outside the allowlist): non-embedded fonts use the browser's system fonts.
   The optional Node canvas of `pdfjs-dist` (`@napi-rs/canvas`, native binary) is removed with a pnpm override.
4. **BFF**: `content-security-policy` joins the forwarded response headers; when the API sends none the BFF sets the
   API's `default-src 'none'; frame-ancestors 'none'`. Chromium still shows a PDF opened in a tab under that CSP
   (checked, also with `sandbox`, which is not added because Firefox and Safari were not checked).

## Consequences
- Server-side previews (thumbnails, HEIC/TIFF, `.p7m` content) and Office conversion stay possible later on the Worker
  (`ProcessDocumentCommand`) without changing the drawer.
- A new version of `pdfjs-dist` updates the copied worker automatically; it must keep the legacy build and the list in
  `scripts/pdfjs-assets.mjs` in step with the package.

## Verification
`pnpm licenses:check` and `pnpm audit` clean; vitest (`preview.test.ts`, BFF CSP); E2E `tenant-documents.spec.ts`
uploads a real PDF and a CSV, checks the canvas has ink, the worker's CSP, the text preview, the CSP of the inline file
and no CSP violation in the console; a JPEG 2000 PDF drawn with the WebAssembly decoder under the same CSPs.
