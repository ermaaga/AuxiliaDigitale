# F24 — Localization

Status: [ ] not started · Tasks: P3-01, P3-05, S-05 

> **Decision D-18:** labels/translations are edited by **System** (platform console).

## Legacy behaviour
- Tables `Languages` (en, it), `ResourceKeys` (unique Key, Category), `ResourceTranslations`. Seed dictionary `LocalizationSeedData.cs` (~500 keys `{Key, (EN, IT)}`) upserted at startup + incremental seed scripts for new keys.
- `ResourceManager` (singleton) loads all translations at boot; lookup falls back to English, then to the key itself. `<T Key="…"/>` component and `Translate` service. Language per user (`User.LanguageId`), registration page language from setting.
- `/system/resources`: list keys (key, category, #translations; filter/sort; row click → detail), add key (key*, category*), delete key. `/system/resources/{id}`: translations grid (language, value), add translation (language, value), delete translation. (No edit-in-place; changes visible after reload of resources — `ReloadResourcesAsync` only called by `AddResourceKeyAsync`.)

## Acceptance criteria
- [ ] All legacy keys and values (EN/IT) available at go-live; tenant-customized values preserved (`is_customized`).
- [ ] `GET /i18n/{lang}` with ETag; cache invalidated immediately on edit (no restart/reload needed).
- [ ] Fallback chain: requested language → tenant default → English → key (and missing keys reported in the editor).
- [ ] `/platform/tenants/{slug}/localization` (System console): search, filter by category, show missing translations, inline edit, add/delete key, add language via data-migration.
- [ ] No hard-coded user-visible strings in new code (EN + IT mandatory).
