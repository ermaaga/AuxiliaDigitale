# ADR 0019 — QR code of the authenticator enrolment (`uqr`)

- Status: Accepted (2026-10-08, approved by the user) · Related: N02, D-22, ADR 0011

## Context
A System user activates the console account by enrolling an authenticator app (TOTP, D-22). The API already returns
the `otpauth://` URI with the secret, but the page showed only the 32-character setup key to type and a link that works
only when the page is opened on the phone. Every authenticator app scans a QR code of that URI.

## Decision
1. Add `uqr` 0.1.3 (MIT, unjs, no dependencies, ~80 KB) to `apps/web`: `encode()` returns the module matrix and the
   component `components/qr-code.tsx` draws it as SVG with React (no HTML string injection), black on white with the
   standard quiet zone, also in the dark theme.
2. The QR is generated in the browser from the URI the page already has: the secret reaches no other service, the CSP
   does not change. The setup key and the link stay as the manual alternative.
3. Alternatives: `qrcode-generator` (MIT, heavier), `QRCoder` on the server (MIT, one more backend dependency and a
   contract change), `qrcode` (pulls `yargs`, `pngjs`).

## Verification
`pnpm licenses:check` (660 packages) and `pnpm audit` clean; the QR of the E2E activation was decoded by an independent
reader (macOS Vision) into the expected `otpauth://` URI.
