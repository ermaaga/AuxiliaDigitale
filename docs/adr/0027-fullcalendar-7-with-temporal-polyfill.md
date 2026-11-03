# ADR 0027 — FullCalendar 7 with `temporal-polyfill`

- Status: Accepted (2026-11-01, approved by the user) · Related: F13, N05 (C-05), ADR 0011, `auxilia-dependency-policy`
  skill

## Context
Dependabot proposed `@fullcalendar/core` and `@fullcalendar/react` 7.1.3 as two separate PRs (#148, #151); neither can
build alone. FullCalendar 7 is a rewrite of the packaging and of the styling:
- the standard plugins are subpaths of `@fullcalendar/react` (`/daygrid`, `/timegrid`, `/list`, `/interaction`,
  `/locales/*`, `/themes/*`); the separate `@fullcalendar/daygrid|timegrid|list|interaction` packages are v6 only;
- a new engine `@full-ui/headless-calendar` (MIT, same repository) and a required peer **`temporal-polyfill`** (MIT,
  fullcalendar/temporal-polyfill, by the FullCalendar author; used internally, not installed globally);
- no bundled CSS: `skeleton.css` + a theme (`themes/classic/theme.css`) are imported, the DOM has no `.fc-*` classes
  any more, the colours are the theme's `--fc-classic-*` variables and per-event `color`/`contrastColor`;
- options renamed (`classNames` → `className`, `slotLabelFormat` → `slotHeaderFormat`, `buttonIcons` →
  `buttonDisplay`), callback types renamed (`*Arg` → `*Info`), button hints written as `aria-label`.

`@fullcalendar/*` standard MIT plugins are in the allowlist; `temporal-polyfill` becomes a direct dependency (a peer
must be installed by the app) and is not, so it needs this ADR.

## Decision
1. Use `@fullcalendar/react` 7 alone (it depends on `@fullcalendar/core` 7 and `@full-ui/headless-calendar`) plus
   `temporal-polyfill` ^1.0.6; drop the v6 plugin packages.
2. Classic theme (`classicThemePlugin`, `skeleton.css`, `themes/classic/theme.css`), its palette defined in
   `app/globals.css` from the design tokens (light and dark) instead of `themes/classic/palette.css`.
3. Appointment colours by status are event `color`/`contrastColor` (`STATUS_COLORS`); connected-calendar events keep
   the account colour with the tint and side bar from `.external-event`.
4. Button hints are empty, so every button's accessible name is its visible text (WCAG 2.5.3; FullCalendar's default
   hints are English in every language and differ from the text, which axe 4.14 reports).
5. Licenses verified from the package `LICENSE` files (MIT for `@fullcalendar/react`, `@fullcalendar/core`,
   `@full-ui/headless-calendar`, `temporal-polyfill`); `pnpm licenses:check` passes. No FullCalendar Premium package.

## Consequences
- One package instead of six; the calendar CSS is loaded with the calendar chunk (dynamic import), no injected styles.
- E2E selectors may no longer rely on `.fc-*` classes (the connected-calendar test uses `.external-event`).
- The skill allowlist (`auxilia-claude-skills`) should list `temporal-polyfill` next to `@fullcalendar/*`.
