# ADR 0018 — Optimistic concurrency with representation ETags

- Status: Accepted (2026-10-06, scope approved by the user in H-04) · Task: H-04c · Parity: F29
- Related: skill `auxilia-api-contract` (Concurrency), ADR 0017

## Context
F29 asks for an `ETag` on single-resource reads, `If-Match` required on writes (`412` on mismatch, `428` when missing)
and a "modified by someone else — reload" message in the web app. The legacy application had no conflict handling:
the last save silently won. The `xmin` token already stops two saves in the same instant (`409 AUX-10010`), but not a
save based on a form opened minutes before another user's change.

The contract skill suggests `ETag = W/"<xmin>"`. Many resources are made of several rows (a client is a person, a
client profile and an account; a case has payments and history), and their detail DTOs carry no version.

## Decision
1. **The ETag is the fingerprint of the representation**: SHA-256 of the JSON the single-resource `GET` returns,
   first 128 bits, base64url, weak (`W/"…"`). Any change the reader could see gives a new version, for composite
   resources too, with no version field in the DTOs (`Api/Infrastructure/ResourceVersioning.cs`, `.WithETag()`).
2. **Writes need `If-Match`** on the routes where a shared record has a `GET` of its own (`.RequireIfMatch()`): clients,
   client tags, employees, services, cases, documents, appointments, requests (delete), tasks, marketing segments,
   lists, templates and campaigns, translation keys, console tenants — 27 operations, each documenting `412` and `428`
   in the OpenAPI contract. The filter reads the resource again through that `GET` (same caller and headers):
   missing header → `428 AUX-10025`, other version → `412 AUX-10011`; `*` matches any version; a resource the caller
   cannot read is left to the write handler (`404`/`403`). `xmin` keeps catching simultaneous saves (`409`).
3. **Out of scope**: the caller's own data (`/me/*`, notification preferences), switches and set replacements
   (sign-in, default employee, specializations, permissions, folders order), binary assets (branding images), settings,
   finished imports. They have no detail read of their own or no concurrent editor.
4. **Web app**: the BFF client remembers the ETag of every `GET` and sends it on the writes listed in
   `lib/api/resource-versions.ts` (a test keeps the list equal to the operations documenting `428`); a write from a page
   that never read the resource (a delete from a list) reads it first; after a write or a `412` the version is
   forgotten, and a `412` refreshes every visible query, so the page already shows the latest data under the
   translated message (`errors.AUX-10011`).
5. **Test hosts**: setting `Concurrency:RequireIfMatch` (default `true`) is `false` only in the base API test factory,
   whose suites predate F29; `ResourceVersioningTests` and the E2E suite run with the requirement.

## Consequences
- **Breaking change** for API clients (a write without `If-Match` now answers `428`): the web app is the only client
  today and changes in the same pull request; the future mobile app follows the contract from the start.
- One extra read per protected write (the same query as the detail page).
- A representation that depends on the moment (e.g. a computed "expired" badge) changes its ETag when the moment
  passes: the save then asks to check the data again — acceptable for a rare, harmless case.
- Check and write are not one transaction: a change in the milliseconds between them is caught by `xmin` (`409`).
