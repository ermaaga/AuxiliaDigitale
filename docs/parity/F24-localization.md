# F24 — Localization

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: P3-01, P3-05, S-05 

> **Decision D-18:** labels/translations are edited by **System** (platform console).

## Legacy behaviour
- Tables `Languages` (en, it), `ResourceKeys` (unique Key, Category), `ResourceTranslations`. Seed dictionary `LocalizationSeedData.cs` (~500 keys `{Key, (EN, IT)}`) upserted at startup + incremental seed scripts for new keys.
- `ResourceManager` (singleton) loads all translations at boot; lookup falls back to English, then to the key itself. `<T Key="…"/>` component and `Translate` service. Language per user (`User.LanguageId`), registration page language from setting.
- `/system/resources`: list keys (key, category, #translations; filter/sort; row click → detail), add key (key*, category*), delete key. `/system/resources/{id}`: translations grid (language, value), add translation (language, value), delete translation. (No edit-in-place; changes visible after reload of resources — `ReloadResourcesAsync` only called by `AddResourceKeyAsync`.)

## Acceptance criteria
- [x] All legacy keys and values (EN/IT) available at go-live; tenant-customized values preserved (`is_customized`). *(P3-01: data-migration `D_20260930_003`, 513 keys with their legacy names, workout keys excluded by D-09; tenant values of the legacy DB imported by E-02.)*
- [x] `GET /i18n/{lang}` with ETag; cache invalidated immediately on edit (no restart/reload needed). *(P3-01: `If-None-Match` → 304, `Cache-Control: no-cache`, tag `t:{slug}:localization` evicted after commit.)*
- [x] Fallback chain: requested language → tenant default → English → key (and missing keys reported in the editor). *(P3-01: `missingLanguages` per key, `filter[missingLanguage]`, per-language missing counts; UI in S-05.)*
- [x] `/platform/tenants/{slug}/localization` (System console): search, filter by category, show missing translations, inline edit, add/delete key, add language via data-migration. *(S-05: page over the P3-01 API — search by key or text, category and "missing in" filters in the URL, missing counts per language, one column per active language with in-place edit (Enter saves, Escape cancels), customised values marked, remove a translation (fallback), new key with first translations, category/description edit, delete with confirmation.)*
- [x] No hard-coded user-visible strings in new code (EN + IT mandatory). *(H-04: scan of the web app — only the development-only `/design-system` showcase; E2E finds every control by its translation key)*
