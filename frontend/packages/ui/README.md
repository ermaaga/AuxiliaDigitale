# @auxilia/ui

Design system: shadcn/ui components (source copied here) on Radix + Tailwind v4, design tokens in `src/styles/tokens.css`.

- Add a component: `cd frontend/packages/ui && pnpm dlx shadcn@latest add <name>`.
- **After adding**, make imports inside `src/` relative (`../lib/utils`), because consuming apps resolve `@/` to their own `src`. Check `package.json` for unexpected new dependencies (allowlist: `auxilia-dependency-policy`).
- Apps import `@auxilia/ui/globals.css` once (root layout) and components as `@auxilia/ui/components/<name>`.
